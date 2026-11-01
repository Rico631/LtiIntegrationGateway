using LtiIntegrationGateway.Features.Contracts;
using LtiIntegrationGateway.Features.Host;
using LtiIntegrationGateway.Infrastructure.Observability;
using LtiIntegrationGateway.Infrastructure.OpenApi;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Contracts;
using LtiIntegrationGateway.Infrastructure.Routing;
using LtiIntegrationGateway.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LtiIntegrationGateway.Features.Configuration;

/// <summary>
/// Сверка действующей конфигурации с применённой: перестраивает наборы входящих маршрутов,
/// прогревает схемы и отчитывается о применённой версии.
/// </summary>
/// <remarks>
/// Версия увеличивается сервисом управления в транзакции изменения. Экземпляр опрашивает её и
/// применяет изменения независимо от других экземпляров; сбой применения оставляет предыдущий
/// набор маршрутов.
/// </remarks>
public sealed class FlowConfigurationApplier(
    IServiceScopeFactory scopeFactory,
    InboundRouteCatalog catalog,
    InboundSchemaCache schemaCache,
    IOptions<InboundRateLimitOptions> rateLimitOptions,
    IOptions<FlowOptions> options,
    GatewayMetrics metrics,
    ILogger<FlowConfigurationApplier> logger) : BackgroundService
{
    private readonly FlowOptions _options = CreateOptions(options);
    private readonly SemaphoreSlim _applyGate = new(1, 1);

    private HashSet<string> _managedKeys = new(StringComparer.Ordinal);
    private long _appliedVersion = -1;
    private string? _applyError;

    /// <summary>Применённая версия конфигурации; используется диагностикой и тестами.</summary>
    public long AppliedVersion => Volatile.Read(ref _appliedVersion);

    /// <summary>Ошибка последнего применения; <c>null</c>, если конфигурация применена.</summary>
    public string? ApplyError => Volatile.Read(ref _applyError);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ApplyAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "сверка конфигурации интеграции {Integration} завершилась ошибкой",
                    _options.Integration);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.PollIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Применяет действующую конфигурацию, если версия изменилась, и публикует состояние экземпляра.
    /// При ошибке проверки новый набор не применяется: предыдущая конфигурация продолжает
    /// обслуживаться, а applied-версия не меняется.
    /// </summary>
    public async Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        await _applyGate.WaitAsync(cancellationToken);

        try
        {
            await ApplyCoreAsync(cancellationToken);
        }
        finally
        {
            _applyGate.Release();
        }
    }

    private async Task ApplyCoreAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();

        var desired = await db.Integrations
            .AsNoTracking()
            .Where(integration => integration.IntegrationId == _options.Integration)
            .Select(integration => (long?)integration.ConfigurationVersion)
            .SingleOrDefaultAsync(cancellationToken) ?? 0;

        var state = await ReadInstanceStateAsync(db, cancellationToken);

        if (desired != AppliedVersion)
        {
            await ApplyVersionAsync(db, desired, state, cancellationToken);
        }

        metrics.SetConfiguration(AppliedVersion, desired);
        await ReportAsync(db, state, cancellationToken);
    }

    private async Task ApplyVersionAsync(
        GatewayDbContext db,
        long desired,
        InstanceStateSnapshot state,
        CancellationToken cancellationToken)
    {
        var versions = await db.ContractVersions
            .AsNoTracking()
            .Include(version => version.InboundChannel)
            .Include(version => version.Operations)
            .Where(version => version.Status == ContractVersionStatus.Active &&
                version.InboundChannel!.IntegrationId == _options.Integration)
            .ToListAsync(cancellationToken);

        var sets = versions
            .Select(version => ContractRouteSets.Build(
                version.Id,
                version.InboundChannel!.Name,
                version.Operations,
                ContractVersionSettings.From(version),
                rateLimitOptions.Value,
                version.InboundChannel.ResponseMode,
                version.InboundChannel.InboundResponseTimeoutMs))
            .ToArray();

        var managedKeys = _managedKeys.ToArray();
        var activation = catalog.Replace(sets, managedKeys);

        if (!activation.Succeeded)
        {
            var problem = string.Join("; ", activation.Problems);

            Volatile.Write(ref _applyError, problem);
            state.ApplyError = problem;

            logger.LogError(
                "конфигурация интеграции {Integration} версии {Version} не применена: {Problems}. " +
                "Продолжает обслуживаться предыдущая конфигурация версии {AppliedVersion}",
                _options.Integration,
                desired,
                problem,
                AppliedVersion);

            return;
        }

        foreach (var version in versions)
        {
            await schemaCache.WarmUpAsync(version.Id, version.Operations, cancellationToken);
        }

        foreach (var key in managedKeys)
        {
            if (!sets.Any(set => set.RevisionKey == key))
            {
                schemaCache.Remove(ParseRevisionKey(key));
            }
        }

        _managedKeys = sets.Select(set => set.RevisionKey).ToHashSet(StringComparer.Ordinal);
        Volatile.Write(ref _applyError, null);
        Volatile.Write(ref _appliedVersion, desired);

        state.ApplyError = null;

        logger.LogInformation(
            "конфигурация интеграции {Integration} применена: версия {Version}, маршрутов {Routes}",
            _options.Integration,
            desired,
            catalog.Snapshot().Count);
    }

    /// <summary>Читает сведения о секретах и сертификатах активной конфигурации своей интеграции.</summary>
    private async Task<InstanceStateSnapshot> ReadInstanceStateAsync(
        GatewayDbContext db,
        CancellationToken cancellationToken)
    {
        await using var secretScope = scopeFactory.CreateAsyncScope();
        var validator = secretScope.ServiceProvider.GetRequiredService<StartupSecretValidator>();
        var missingSecrets = await validator.FindMissingAsync(_options.Integration, cancellationToken);

        var certificateKeys = await db.ChannelRevisions
            .AsNoTracking()
            .Where(revision => revision.Status == ChannelRevisionStatus.Active &&
                revision.AuthType == ChannelAuthType.ClientCertificate &&
                revision.OutboundChannel!.IntegrationId == _options.Integration)
            .Select(revision => revision.ConfigKey)
            .ToListAsync(cancellationToken);

        certificateKeys.AddRange(await db.ContractVersions
            .AsNoTracking()
            .Where(version => version.Status == ContractVersionStatus.Active &&
                version.AuthType == ChannelAuthType.ClientCertificate &&
                version.InboundChannel!.IntegrationId == _options.Integration)
            .Select(version => version.ConfigKey)
            .ToListAsync(cancellationToken));

        var certificates = secretScope.ServiceProvider.GetRequiredService<IChannelCertificateStore>();

        var expiresAt = certificateKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => certificates.GetInfo(key).ExpiresAt)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .DefaultIfEmpty()
            .Min();

        return new InstanceStateSnapshot
        {
            SecretConfigured = missingSecrets.Count == 0,
            CertificateExpiresAt = expiresAt == default ? null : expiresAt,
            ApplyError = ApplyError,
        };
    }

    /// <summary>Публикует применённую версию, состояние секретов и ошибку применения.</summary>
    private async Task ReportAsync(
        GatewayDbContext db,
        InstanceStateSnapshot state,
        CancellationToken cancellationToken)
    {
        // Upsert: фоновый цикл и явный вызов применения могут отчитаться одновременно. Отчёт
        // выполняется и без изменения версии, чтобы ReportedAt оставался актуальным.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "FlowInstanceStates"
                ("Id", "IntegrationId", "InstanceId", "AppliedVersion", "SecretConfigured",
                 "CertificateExpiresAt", "ApplyError", "ReportedAt")
            VALUES
                ({Guid.NewGuid()}, {_options.Integration}, {_options.InstanceId}, {AppliedVersion},
                 {state.SecretConfigured}, CAST({state.CertificateExpiresAt} AS timestamptz),
                 CAST({state.ApplyError} AS character varying(4096)), {DateTimeOffset.UtcNow})
            ON CONFLICT ("IntegrationId", "InstanceId")
            DO UPDATE SET
                "AppliedVersion" = EXCLUDED."AppliedVersion",
                "SecretConfigured" = EXCLUDED."SecretConfigured",
                "CertificateExpiresAt" = EXCLUDED."CertificateExpiresAt",
                "ApplyError" = EXCLUDED."ApplyError",
                "ReportedAt" = EXCLUDED."ReportedAt"
            """,
            cancellationToken);
    }

    private static Guid ParseRevisionKey(string revisionKey)
    {
        const string prefix = "contract:";

        return revisionKey.StartsWith(prefix, StringComparison.Ordinal) &&
            Guid.TryParse(revisionKey[prefix.Length..], out var id)
                ? id
                : Guid.Empty;
    }

    private static FlowOptions CreateOptions(IOptions<FlowOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var value = options.Value;
        value.Validate();

        return value;
    }

    private sealed class InstanceStateSnapshot
    {
        public bool SecretConfigured { get; init; }

        public DateTimeOffset? CertificateExpiresAt { get; init; }

        public string? ApplyError { get; set; }
    }
}
