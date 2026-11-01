using LtiIntegrationGateway.Infrastructure.Management;
using LtiIntegrationGateway.Infrastructure.OpenApi;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Contracts;
using LtiIntegrationGateway.Infrastructure.Rebus;
using LtiIntegrationGateway.Infrastructure.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LtiIntegrationGateway.Features.Contracts.PublishContractVersion;

/// <summary>
/// Публикация версии контракта: извлечение операций, перенос настроек из предыдущей версии канала
/// и проверки конфликтов (ADR §7.3).
/// </summary>
public sealed record PublishContractVersionCommand(string ChannelName, int Version);

public sealed record PublishContractVersionResult(int? Version, IReadOnlyList<string> Problems);

public sealed class PublishContractVersionHandler(
    GatewayDbContext db,
    IOpenApiDocumentReader reader,
    InboundRouteSetValidator routeValidator,
    ManagementAuditWriter audit,
    IOptions<InboundRateLimitOptions> rateLimitOptions)
{
    public async Task<PublishContractVersionResult> HandleAsync(
        PublishContractVersionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var version = await db.ContractVersions
            .Include(entity => entity.InboundChannel)
            .Include(entity => entity.Operations)
            .SingleOrDefaultAsync(
                entity => entity.InboundChannel!.Name == command.ChannelName && entity.Version == command.Version,
                cancellationToken);

        var target = $"{command.ChannelName}:v{command.Version}";

        if (version is null)
        {
            await audit.RecordAsync("PublishContract", target, false, "version: версия контракта не найдена", cancellationToken);

            return new PublishContractVersionResult(null, ["version: версия контракта не найдена"]);
        }

        if (version.Status != ContractVersionStatus.Draft)
        {
            await audit.RecordAsync("PublishContract", target, false, "version: опубликованная версия неизменяема", cancellationToken);

            return new PublishContractVersionResult(version.Version, ["version: опубликованная версия неизменяема"]);
        }

        // Очередь получателя в настройках версии обязательна всегда, даже если все операции отключены
        // (ADR §15.2); переопределение операции её не заменяет.
        if (string.IsNullOrWhiteSpace(version.RecipientQueue))
        {
            await audit.RecordAsync("PublishContract", target, false, "queue: очередь получателя обязательна", cancellationToken);

            return new PublishContractVersionResult(version.Version, ["queue: очередь получателя обязательна"]);
        }

        // Имя очереди получателя — вычисляемое имя продукта <product_id>.inbox (ADR §10.2). Проверка
        // выполняется до чтения документа: несоответствие формату отклоняет публикацию, операции
        // не создаются.
        if (!QueueNames.IsProductInbox(version.RecipientQueue))
        {
            var problem = "queue: очередь получателя должна иметь формат <product_id>.inbox";

            await audit.RecordAsync("PublishContract", target, false, problem, cancellationToken);

            return new PublishContractVersionResult(version.Version, [problem]);
        }

        var read = await reader.ReadAsync(version.Document, cancellationToken);
        var problems = read.Problems.ToList();
        IReadOnlyList<ContractOperation> planned = [];

        if (read.Succeeded)
        {
            var settings = ContractVersionSettings.From(version);
            var previousOperations = await LoadPreviousOperationsAsync(version, cancellationToken);

            planned = PlanOperations(version.Id, read, previousOperations);

            // Дополнительно проверяется действующая очередь каждой включённой операции: при заданной
            // очереди версии ошибка возможна только при некорректных переопределениях операции.
            // Версия, в которой все операции отключены, публикуется при заданной очереди версии.
            problems.AddRange(FindQueueProblems(planned, settings));
            problems.AddRange(FindDuplicateOperationIds(read.Operations));

            var activeSets = await LoadOtherActiveSetsAsync(version, cancellationToken);
            var candidate = ContractRouteSets.Build(version.Id, version.InboundChannel!.Name, read.Operations);

            problems.AddRange(routeValidator.Validate(candidate, activeSets));
        }

        if (problems.Count > 0)
        {
            await audit.RecordAsync("PublishContract", target, false, string.Join("; ", problems), cancellationToken);

            return new PublishContractVersionResult(version.Version, problems);
        }

        db.ContractOperations.RemoveRange(version.Operations);

        foreach (var operation in planned)
        {
            db.ContractOperations.Add(operation);
        }

        version.Status = ContractVersionStatus.Published;
        version.PublishedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync("PublishContract", target, true, $"{read.Operations.Count} operations", cancellationToken);

        return new PublishContractVersionResult(version.Version, []);
    }

    /// <summary>
    /// Операции предыдущей версии канала: максимальный номер меньше текущего, кроме черновиков.
    /// Настройки совпавшей операции переносятся; для новой операции действуют значения по умолчанию.
    /// </summary>
    private async Task<IReadOnlyList<ContractOperation>> LoadPreviousOperationsAsync(
        ContractVersion version,
        CancellationToken cancellationToken)
    {
        var previous = await db.ContractVersions
            .AsNoTracking()
            .Include(entity => entity.Operations)
            .Where(entity =>
                entity.InboundChannelId == version.InboundChannelId &&
                entity.Version < version.Version &&
                entity.Status != ContractVersionStatus.Draft)
            .OrderByDescending(entity => entity.Version)
            .FirstOrDefaultAsync(cancellationToken);

        return previous?.Operations ?? [];
    }

    private static IReadOnlyList<ContractOperation> PlanOperations(
        Guid contractVersionId,
        OpenApiReadResult read,
        IReadOnlyList<ContractOperation> previous)
    {
        var planned = new List<ContractOperation>(read.Operations.Count);

        foreach (var operation in read.Operations)
        {
            var source = FindPreviousOperation(previous, operation);

            planned.Add(new ContractOperation
            {
                Id = Guid.NewGuid(),
                ContractVersionId = contractVersionId,
                HttpMethod = operation.HttpMethod,
                Template = operation.Template,
                OperationId = operation.OperationId,
                RequestSchemas = read.RequestSchemas.GetValueOrDefault(
                    $"{operation.HttpMethod} {operation.Template}"),
                Enabled = source?.Enabled ?? true,
                RecipientQueue = source?.RecipientQueue,
            });
        }

        return planned;
    }

    /// <summary>
    /// Совпадение операций для переноса настроек: маршрут (метод + шаблон) обязан совпадать.
    /// Если <c>operationId</c> задан у обеих операций, он должен совпадать; fallback по паре
    /// «метод + шаблон» применяется, только если <c>operationId</c> отсутствует хотя бы у одной из
    /// них. Изменённый <c>operationId</c> при том же маршруте означает новую операцию — настройки
    /// не переносятся (spec `contract-registry`).
    /// </summary>
    private static ContractOperation? FindPreviousOperation(
        IReadOnlyList<ContractOperation> previous,
        OpenApiOperationDefinition operation)
    {
        var candidate = previous.FirstOrDefault(previousOperation => SameRoute(previousOperation, operation));

        if (candidate is null)
        {
            return null;
        }

        var operationHasId = !string.IsNullOrWhiteSpace(operation.OperationId);
        var candidateHasId = !string.IsNullOrWhiteSpace(candidate.OperationId);

        if (operationHasId && candidateHasId)
        {
            return string.Equals(candidate.OperationId, operation.OperationId, StringComparison.Ordinal)
                ? candidate
                : null;
        }

        return candidate;
    }

    private static bool SameRoute(ContractOperation candidate, OpenApiOperationDefinition operation) =>
        string.Equals(candidate.HttpMethod.Trim(), operation.HttpMethod.Trim(), StringComparison.OrdinalIgnoreCase) &&
        string.Equals(candidate.Template.Trim(), operation.Template.Trim(), StringComparison.Ordinal);

    private static IEnumerable<string> FindQueueProblems(
        IEnumerable<ContractOperation> operations,
        ContractVersionSettings settings) =>
        operations
            .Where(operation => operation.Enabled)
            .Where(operation => ContractOperationSettings.ResolveRecipientQueue(operation, settings) is null)
            .Select(operation =>
                $"queue: для операции '{operation.HttpMethod} {operation.Template}' не задана очередь получателя");

    private async Task<IReadOnlyList<InboundRouteSet>> LoadOtherActiveSetsAsync(
        ContractVersion version,
        CancellationToken cancellationToken)
    {
        var activeVersions = await db.ContractVersions
            .AsNoTracking()
            .Include(entity => entity.InboundChannel)
            .Include(entity => entity.Operations)
            .Where(entity => entity.Status == ContractVersionStatus.Active && entity.InboundChannelId != version.InboundChannelId)
            .ToListAsync(cancellationToken);

        return activeVersions
            .Select(active => ContractRouteSets.Build(
                active.Id,
                active.InboundChannel!.Name,
                active.Operations,
                ContractVersionSettings.From(active),
                rateLimitOptions.Value))
            .ToArray();
    }

    private static IEnumerable<string> FindDuplicateOperationIds(
        IReadOnlyList<OpenApiOperationDefinition> operations) =>
        operations
            .Where(operation => operation.OperationId is not null)
            .GroupBy(operation => operation.OperationId!, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"operation_id: '{group.Key}' повторяется в документе");

}
