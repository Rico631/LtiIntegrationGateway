using LtiIntegrationGateway.Features.Channels.UpdateChannelDraft;
using LtiIntegrationGateway.Infrastructure.Management;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;
using LtiIntegrationGateway.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace LtiIntegrationGateway.Features.Channels.GetChannelHealth;

/// <summary>Запрос состояния канала по имени.</summary>
public sealed record GetChannelHealthQuery(
    string ChannelName,
    string IntegrationId = IntegrationDefaults.Default);

/// <summary>
/// Ошибка обмена по каналу для диагностического ответа: только время, направление, код и исход.
/// </summary>
/// <remarks>
/// Тела сообщений, заголовки и тексты ошибок в результат не попадают, хотя хранятся в журналах
/// (ADR §13): ответ управляющего API не должен раскрывать содержимое обмена.
/// </remarks>
public sealed record ChannelHealthErrorEntry(
    DateTimeOffset OccurredAt,
    string Direction,
    string ErrorCode,
    int HttpStatus,
    string Outcome);

/// <summary>Состояние канала: активная ревизия, настройка секрета, срок сертификата и последние ошибки.</summary>
public sealed record ChannelHealthDetails(
    string ChannelName,
    string State,
    int ActiveRevision,
    bool SecretConfigured,
    string SecretConfigKey,
    DateTimeOffset? CertificateExpiresAt,
    IReadOnlyList<ChannelHealthErrorEntry> Errors,
    DateTimeOffset CheckedAt,
    long ConfigurationVersion,
    IReadOnlyList<FlowInstanceStateDetails> FlowInstances);

/// <summary>
/// Применённое состояние экземпляра сервиса потоков: версия конфигурации, время отчёта и признаки
/// расхождения и устаревания.
/// </summary>
public sealed record FlowInstanceStateDetails(
    string InstanceId,
    long AppliedVersion,
    bool UpToDate,
    bool Stale,
    DateTimeOffset ReportedAt,
    bool SecretConfigured,
    DateTimeOffset? CertificateExpiresAt,
    string? ApplyError);

/// <summary>Результат чтения состояния; <see cref="Health"/> равен <c>null</c> для неизвестного канала.</summary>
public sealed record GetChannelHealthResult(ChannelHealthDetails? Health, IReadOnlyList<string> Problems);

/// <summary>
/// Чтение состояния канала для диагностики (ADR §13, §15.2): наличие активной ревизии, признак
/// настроенного секрета, срок действия клиентского сертификата и последние ошибки обмена.
/// </summary>
/// <remarks>
/// Срез ничего не меняет: единственная запись — аудит управляющей операции. Отсутствие секрета не
/// является ошибкой вызова, это диагностическое состояние в ответе. Значение секрета не читается в
/// результат и не попадает в аудит: возвращается только имя ключа конфигурации.
/// </remarks>
public sealed class GetChannelHealthHandler(
    GatewayDbContext db,
    ISecretStore secrets,
    IChannelCertificateStore certificates,
    ManagementAuditWriter audit,
    TimeProvider timeProvider,
    ILogger<GetChannelHealthHandler> logger)
{
    /// <summary>Имя операции в аудите управляющих операций.</summary>
    public const string AuditOperation = "GetChannelHealth";

    /// <summary>Состояние: активная ревизия канала найдена.</summary>
    public const string ActiveState = "ACTIVE";

    /// <summary>Состояние: у канала нет активной ревизии.</summary>
    public const string NoActiveRevisionState = "NO_ACTIVE_REVISION";

    /// <summary>Направление ошибки: исходящий вызов внешней системы.</summary>
    public const string OutboundDirection = "outbound";

    /// <summary>Направление ошибки: входящий вызов шлюза.</summary>
    public const string InboundDirection = "inbound";

    /// <summary>Максимальное число ошибок одного направления в ответе.</summary>
    public const int MaxErrorsPerDirection = 5;

    /// <summary>Порог устаревания отчёта экземпляра: отчёт старше порога помечается как устаревший.</summary>
    public static readonly TimeSpan StaleReportThreshold = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Размер окна выборки из каждого журнала: SQL берёт последние записи по времени и сортирует
    /// в памяти только это окно — при объединении двух направлений. Отдельная политика хранения
    /// не вводится (ADR §11.2), поэтому окно ограничивает объём чтения диагностического запроса.
    /// </summary>
    public const int ErrorWindow = 64;

    public async Task<GetChannelHealthResult> HandleAsync(
        GetChannelHealthQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var channel = await ChannelQueries
            .WithRevisions(db)
            .AsNoTracking()
            .SingleOrDefaultAsync(
                entity => entity.IntegrationId == query.IntegrationId && entity.Name == query.ChannelName,
                cancellationToken);

        if (channel is null)
        {
            IReadOnlyList<string> problems = ["channel: канал не найден"];

            await AuditAsync(query.ChannelName, false, problems, cancellationToken);

            return new GetChannelHealthResult(null, problems);
        }

        var active = channel.Revisions.SingleOrDefault(
            revision => revision.Status == ChannelRevisionStatus.Active);

        var configuration = active is null ? null : ChannelRevisionWriter.Read(active);
        var secret = EvaluateSecret(configuration);
        var checkedAt = timeProvider.GetUtcNow();

        if (secret.Problem is not null)
        {
            // Причина не содержит значения материала или пароля и помогает диагностике.
            logger.LogInformation(
                "Канал {Channel}: клиентский сертификат непригоден: {Problem}",
                channel.Name,
                secret.Problem);
        }

        var configurationVersion = await db.Integrations
            .AsNoTracking()
            .Where(integration => integration.IntegrationId == query.IntegrationId)
            .Select(integration => (long?)integration.ConfigurationVersion)
            .SingleOrDefaultAsync(cancellationToken) ?? 0;

        var instances = await ReadFlowInstancesAsync(
            query.IntegrationId,
            configurationVersion,
            checkedAt,
            cancellationToken);

        var health = new ChannelHealthDetails(
            channel.Name,
            active is null ? NoActiveRevisionState : ActiveState,
            active?.Number ?? 0,
            secret.Configured,
            secret.ConfigKey,
            secret.CertificateExpiresAt,
            await ReadRecentErrorsAsync(query.IntegrationId, channel.Name, cancellationToken),
            checkedAt,
            configurationVersion,
            instances);

        await AuditAsync(channel.Name, true, [], cancellationToken);

        return new GetChannelHealthResult(health, []);
    }

    /// <summary>
    /// Проверяет обеспеченность активной ревизии секретом по типу авторизации: <c>none</c> не требует
    /// секрета, для <c>bearer</c>/<c>api_key</c>/<c>basic</c> значение ищется в хранилище секретов, для
    /// <c>client_certificate</c> загружается материал сертификата, а срок берётся из него.
    /// </summary>
    private (bool Configured, string ConfigKey, DateTimeOffset? CertificateExpiresAt, string? Problem)
        EvaluateSecret(ChannelConfiguration? configuration)
    {
        if (configuration is null)
        {
            return (false, string.Empty, null, null);
        }

        var configKey = configuration.ConfigKey ?? string.Empty;

        if (configuration.AuthType == ChannelAuthType.None)
        {
            return (true, configKey, null, null);
        }

        // Пустой ключ при типе, требующем секрет, означает, что материал взять неоткуда.
        if (string.IsNullOrWhiteSpace(configuration.ConfigKey))
        {
            return (false, configKey, null, null);
        }

        if (configuration.AuthType == ChannelAuthType.ClientCertificate)
        {
            // Диагностика не требует валидности: срок возвращается и для истёкшего сертификата,
            // а пригодность к использованию определяется отдельно.
            var info = certificates.GetInfo(configuration.ConfigKey);

            return (info.Found && info.Valid, configKey, info.ExpiresAt, info.Problem);
        }

        return (secrets.Find(configuration.ConfigKey) is not null, configKey, null, null);
    }

    /// <summary>Читает отчёты экземпляров интеграции и вычисляет признаки расхождения и устаревания.</summary>
    private async Task<IReadOnlyList<FlowInstanceStateDetails>> ReadFlowInstancesAsync(
        string integrationId,
        long configurationVersion,
        DateTimeOffset checkedAt,
        CancellationToken cancellationToken)
    {
        var states = await db.FlowInstanceStates
            .AsNoTracking()
            .Where(state => state.IntegrationId == integrationId)
            .OrderBy(state => state.InstanceId)
            .ToListAsync(cancellationToken);

        return states
            .Select(state => new FlowInstanceStateDetails(
                state.InstanceId,
                state.AppliedVersion,
                state.AppliedVersion >= configurationVersion,
                checkedAt - state.ReportedAt > StaleReportThreshold,
                state.ReportedAt,
                state.SecretConfigured,
                state.CertificateExpiresAt,
                state.ApplyError))
            .ToArray();
    }

    /// <summary>Читает не более пяти последних ошибок outbound-направления, от новых к старым.</summary>
    private async Task<IReadOnlyList<ChannelHealthErrorEntry>> ReadRecentErrorsAsync(
        string integrationId,
        string channelName,
        CancellationToken cancellationToken)
    {
        var outbound = await db.OutboundMessageLogs
            .AsNoTracking()
            .Where(log => log.IntegrationId == integrationId &&
                log.ChannelName == channelName &&
                log.ErrorCode != null)
            .OrderByDescending(log => log.CreatedAt)
            .Select(log => new
            {
                log.ErrorCode,
                log.Outcome,
                log.HttpStatusCode,
                log.CreatedAt,
            })
            .Take(ErrorWindow)
            .ToListAsync(cancellationToken);

        return outbound
            .Select(log => new ChannelHealthErrorEntry(
                log.CreatedAt,
                OutboundDirection,
                log.ErrorCode!,
                log.HttpStatusCode ?? 0,
                log.Outcome ?? string.Empty))
            .OrderByDescending(entry => entry.OccurredAt)
            .Take(MaxErrorsPerDirection)
            .ToArray();
    }

    private Task AuditAsync(
        string channelName,
        bool succeeded,
        IReadOnlyList<string> problems,
        CancellationToken cancellationToken) =>
        audit.RecordAsync(
            AuditOperation,
            channelName,
            succeeded,
            problems.Count == 0 ? null : string.Join("; ", problems),
            cancellationToken);
}




