using LtiIntegrationGateway.Infrastructure.Management;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Contracts;
using LtiIntegrationGateway.Infrastructure.Rebus;
using LtiIntegrationGateway.Infrastructure.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LtiIntegrationGateway.Features.Contracts.UpdateContractOperationSettings;

/// <summary>
/// Изменяемые настройки операции: <c>null</c> означает «поле не передано и не меняется».
/// Для очереди переданная пустая строка снимает переопределение (ADR §7.3, §15.2).
/// </summary>
public sealed record ContractOperationSettingsPatch(
    bool? Enabled,
    string? RecipientQueue)
{
    /// <summary>Признак отсутствия изменений: ни одно поле не передано.</summary>
    public bool IsEmpty =>
        Enabled is null &&
        RecipientQueue is null;
}

/// <summary>Изменение настроек операции версии контракта.</summary>
public sealed record UpdateContractOperationSettingsCommand(
    string ChannelName,
    int Version,
    string OperationId,
    ContractOperationSettingsPatch Settings);

/// <summary>
/// Результат изменения настроек: <c>NotFound</c> — неизвестная версия или операция, ответ управляющего
/// API должен быть <c>404</c>; остальные отказы возвращаются полным списком проблем.
/// </summary>
public sealed record UpdateContractOperationSettingsResult(
    int? Version,
    bool NotFound,
    IReadOnlyList<string> Problems);

/// <summary>
/// Настройки операции — изменяемый слой поверх неизменяемого документа версии: изменение активно для
/// работающих маршрутов без перезапуска, для версии в состоянии superseded запрещено (ADR §7.3).
/// </summary>
/// <remarks>
/// Операции извлекаются из документа при публикации, поэтому для черновика запрос возвращает
/// <c>NotFound</c>: адресовать операцию до публикации нельзя.
/// </remarks>
public sealed class UpdateContractOperationSettingsHandler(
    GatewayDbContext db,
    InboundRouteCatalog catalog,
    IOptions<InboundRateLimitOptions> rateLimitOptions,
    ManagementAuditWriter audit)
{
    /// <summary>Имя операции в аудите управляющих операций.</summary>
    public const string AuditOperation = "UpdateContractOperationSettings";

    public async Task<UpdateContractOperationSettingsResult> HandleAsync(
        UpdateContractOperationSettingsCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Settings);

        var target = $"{command.ChannelName}:v{command.Version}:{command.OperationId}";

        var version = await db.ContractVersions
            .Include(entity => entity.InboundChannel)
            .Include(entity => entity.Operations)
            .SingleOrDefaultAsync(
                entity => entity.InboundChannel!.Name == command.ChannelName && entity.Version == command.Version,
                cancellationToken);

        if (version is null)
        {
            return await RejectAsync(target, null, true, "version: версия контракта не найдена", cancellationToken);
        }

        if (version.Status == ContractVersionStatus.Superseded)
        {
            return await RejectAsync(
                target,
                version.Version,
                false,
                "version: отменённая версия неизменяема",
                cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(command.OperationId))
        {
            return await RejectAsync(
                target,
                version.Version,
                false,
                "operation_id: идентификатор операции обязателен",
                cancellationToken);
        }

        var operation = version.Operations.FirstOrDefault(candidate =>
            string.Equals(candidate.OperationId, command.OperationId, StringComparison.Ordinal));

        if (operation is null)
        {
            return await RejectAsync(
                target,
                version.Version,
                true,
                "operation: операция не найдена в версии контракта",
                cancellationToken);
        }

        var problems = Validate(command.Settings);

        if (problems.Count > 0)
        {
            return await RejectAsync(target, version.Version, false, string.Join("; ", problems), cancellationToken);
        }

        var original = (operation.Enabled, operation.RecipientQueue);
        var previousSet = version.Status == ContractVersionStatus.Active ? BuildSet(version) : null;

        Apply(operation, command.Settings);

        if (previousSet is not null)
        {
            var activation = catalog.Activate(BuildSet(version));

            if (!activation.Succeeded)
            {
                // Каталог не принял набор: прежний набор остаётся активным, база не меняется.
                (operation.Enabled, operation.RecipientQueue) = original;

                return await RejectAsync(
                    target,
                    version.Version,
                    false,
                    string.Join("; ", activation.Problems),
                    cancellationToken);
            }
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            if (previousSet is not null)
            {
                catalog.Activate(previousSet);
            }

            throw;
        }

        await audit.RecordAsync(AuditOperation, target, true, null, cancellationToken);

        return new UpdateContractOperationSettingsResult(version.Version, false, []);
    }

    private static IReadOnlyList<string> Validate(ContractOperationSettingsPatch settings)
    {
        var problems = new List<string>();

        if (settings.IsEmpty)
        {
            problems.Add("settings: не передано ни одного поля настроек");
        }

        // Пустая строка снимает переопределение, отсутствие поля значение не меняет; непустой адрес
        // обязан иметь формат вычисленного имени очереди продукта (ADR §10.2).
        var recipientQueue = settings.RecipientQueue?.Trim();

        if (!string.IsNullOrEmpty(recipientQueue) && !QueueNames.IsProductInbox(recipientQueue))
        {
            problems.Add("queue: очередь операции должна иметь формат <product_id>.inbox");
        }

        return problems;
    }

    private static void Apply(ContractOperation operation, ContractOperationSettingsPatch settings)
    {
        if (settings.Enabled is { } enabled)
        {
            operation.Enabled = enabled;
        }

        if (settings.RecipientQueue is not null)
        {
            operation.RecipientQueue = string.IsNullOrWhiteSpace(settings.RecipientQueue)
                ? null
                : settings.RecipientQueue.Trim();
        }
    }

    private InboundRouteSet BuildSet(ContractVersion version) =>
        ContractRouteSets.Build(
            version.Id,
            version.InboundChannel!.Name,
            version.Operations,
            ContractVersionSettings.From(version),
            rateLimitOptions.Value);

    private async Task<UpdateContractOperationSettingsResult> RejectAsync(
        string target,
        int? version,
        bool notFound,
        string details,
        CancellationToken cancellationToken)
    {
        await audit.RecordAsync(AuditOperation, target, false, details, cancellationToken);

        return new UpdateContractOperationSettingsResult(version, notFound, [details]);
    }

}
