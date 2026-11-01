using LtiIntegrationGateway.Infrastructure.Management;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Contracts;
using LtiIntegrationGateway.Infrastructure.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LtiIntegrationGateway.Features.Contracts.ActivateContractVersion;

/// <summary>Активация версии контракта.</summary>
public sealed record ActivateContractVersionCommand(
    string ChannelName,
    int Version,
    string IntegrationId = IntegrationDefaults.Default);

public sealed record ActivateContractVersionResult(int? Version, IReadOnlyList<string> Problems);

/// <summary>
/// Активация версии контракта как фиксация желаемого состояния: проверка конфликтов маршрутов по
/// данным хранилища, ответ после коммита и без изменения runtime-состояния сервиса управления.
/// </summary>
/// <remarks>
/// Маршруты в экземплярах потоков регистрируются асинхронно: экземпляры сверяют версию конфигурации
/// и применяют её независимо. Поэтому успешный ответ означает «зафиксировано и прошло проверки», а
/// не «маршруты уже обслуживаются». Гонку активаций разрешает частичный уникальный индекс: вторая
/// транзакция получает нарушение уникальности и возвращает проблему <c>conflict</c>.
/// </remarks>
public sealed class ActivateContractVersionHandler(
    GatewayDbContext db,
    InboundRouteSetValidator routeValidator,
    ManagementAuditWriter audit)
{
    public async Task<ActivateContractVersionResult> HandleAsync(
        ActivateContractVersionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var target = $"{command.ChannelName}:v{command.Version}";

        var version = await db.ContractVersions
            .Include(entity => entity.InboundChannel)
            .Include(entity => entity.Operations)
            .SingleOrDefaultAsync(
                entity => entity.InboundChannel!.IntegrationId == command.IntegrationId &&
                          entity.InboundChannel!.Name == command.ChannelName &&
                          entity.Version == command.Version,
                cancellationToken);

        if (version is null)
        {
            await audit.RecordAsync("ActivateContract", target, false, "version: версия контракта не найдена", cancellationToken);

            return new ActivateContractVersionResult(null, ["version: версия контракта не найдена"]);
        }

        if (version.Status == ContractVersionStatus.Draft)
        {
            await audit.RecordAsync("ActivateContract", target, false, "version: версия не опубликована", cancellationToken);

            return new ActivateContractVersionResult(version.Version, ["version: версия не опубликована"]);
        }

        var problems = await ValidateRoutesAsync(version, cancellationToken);

        if (problems.Count > 0)
        {
            await audit.RecordAsync("ActivateContract", target, false, string.Join("; ", problems), cancellationToken);

            return new ActivateContractVersionResult(version.Version, problems);
        }

        var previous = await db.ContractVersions
            .SingleOrDefaultAsync(
                entity => entity.InboundChannelId == version.InboundChannelId &&
                          entity.Status == ContractVersionStatus.Active &&
                          entity.Id != version.Id,
                cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            // Сначала фиксируем снятие предыдущей версии: частичный уникальный индекс не допускает
            // двух активных версий даже внутри одной транзакции.
            if (previous is not null)
            {
                previous.Status = ContractVersionStatus.Superseded;
                await db.SaveChangesAsync(cancellationToken);
            }

            version.Status = ContractVersionStatus.Active;
            version.ActivatedAt = DateTimeOffset.UtcNow;

            await IntegrationRegistry.BumpAsync(db, command.IntegrationId, cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // Транзакция уже прервана базой: откат и очистка состояния контекста нужны, чтобы аудит
            // записывался отдельным изменением, а не повтором отклонённого UPDATE.
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();

            var conflict = "conflict: активная версия контракта уже зафиксирована другой операцией";

            await audit.RecordAsync("ActivateContract", target, false, conflict, cancellationToken);

            return new ActivateContractVersionResult(version.Version, [conflict]);
        }

        await audit.RecordAsync("ActivateContract", target, true, null, cancellationToken);

        return new ActivateContractVersionResult(version.Version, []);
    }

    /// <summary>
    /// Проверяет маршруты версии на конфликты внутри набора и с активными версиями других каналов
    /// той же интеграции. Активная версия этого же канала заменяется и в проверке не участвует.
    /// </summary>
    private async Task<IReadOnlyList<string>> ValidateRoutesAsync(
        ContractVersion version,
        CancellationToken cancellationToken)
    {
        var activeVersions = await db.ContractVersions
            .AsNoTracking()
            .Include(entity => entity.InboundChannel)
            .Include(entity => entity.Operations)
            .Where(entity => entity.Status == ContractVersionStatus.Active &&
                entity.InboundChannel!.IntegrationId == version.InboundChannel!.IntegrationId &&
                entity.InboundChannelId != version.InboundChannelId)
            .ToListAsync(cancellationToken);

        var activeSets = activeVersions
            .Select(active => ContractRouteSets.Build(
                active.Id,
                active.InboundChannel!.Name,
                active.Operations,
                ContractVersionSettings.From(active),
                new InboundRateLimitOptions()))
            .ToArray();

        var candidate = CreateSet(version);

        return routeValidator.Validate(candidate, activeSets);
    }

    private static InboundRouteSet CreateSet(ContractVersion version) =>
        ContractRouteSets.Build(
            version.Id,
            version.InboundChannel!.Name,
            version.Operations,
            ContractVersionSettings.From(version),
            new InboundRateLimitOptions());

    private static bool IsUniqueViolation(Exception? exception) =>
        exception is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } ||
        (exception?.InnerException is not null && IsUniqueViolation(exception.InnerException));
}
