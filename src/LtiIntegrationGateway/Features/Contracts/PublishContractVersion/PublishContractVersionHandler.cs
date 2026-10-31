using LtiIntegrationGateway.Infrastructure.Management;
using LtiIntegrationGateway.Infrastructure.OpenApi;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities;
using LtiIntegrationGateway.Infrastructure.Routing;
using Microsoft.EntityFrameworkCore;

namespace LtiIntegrationGateway.Features.Contracts;

/// <summary>Публикация версии контракта: извлечение операций и проверки конфликтов.</summary>
public sealed record PublishContractVersionCommand(string ChannelName, int Version);

public sealed record PublishContractVersionResult(int? Version, IReadOnlyList<string> Problems);

public sealed class PublishContractVersionHandler(
    GatewayDbContext db,
    IOpenApiDocumentReader reader,
    InboundRouteSetValidator routeValidator,
    ManagementAuditWriter audit)
{
    public async Task<PublishContractVersionResult> HandleAsync(
        PublishContractVersionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var version = await db.ContractVersions
            .Include(entity => entity.Channel)
            .Include(entity => entity.Operations)
            .SingleOrDefaultAsync(
                entity => entity.Channel!.Name == command.ChannelName && entity.Version == command.Version,
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

        if (string.IsNullOrWhiteSpace(version.RecipientQueue))
        {
            await audit.RecordAsync("PublishContract", target, false, "queue: очередь получателя обязательна", cancellationToken);

            return new PublishContractVersionResult(version.Version, ["queue: очередь получателя обязательна"]);
        }

        var read = await reader.ReadAsync(version.Document, cancellationToken);
        var problems = read.Problems.ToList();

        if (read.Succeeded)
        {
            problems.AddRange(FindDuplicateOperationIds(read.Operations));

            var activeSets = await LoadOtherActiveSetsAsync(version, cancellationToken);
            var candidate = ContractRouteSets.Build(version.Id, version.Channel!.Name, read.Operations);

            problems.AddRange(routeValidator.Validate(candidate, activeSets));
        }

        if (problems.Count > 0)
        {
            await audit.RecordAsync("PublishContract", target, false, Truncate(string.Join("; ", problems)), cancellationToken);

            return new PublishContractVersionResult(version.Version, problems);
        }

        db.ContractOperations.RemoveRange(version.Operations);

        foreach (var operation in read.Operations)
        {
            db.ContractOperations.Add(new ContractOperation
            {
                Id = Guid.NewGuid(),
                ContractVersionId = version.Id,
                HttpMethod = operation.HttpMethod,
                Template = operation.Template,
                OperationId = operation.OperationId,
                RequestSchemas = read.RequestSchemas.GetValueOrDefault(
                    $"{operation.HttpMethod} {operation.Template}"),
            });
        }

        version.Status = ContractVersionStatus.Published;
        version.PublishedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync("PublishContract", target, true, $"{read.Operations.Count} operations", cancellationToken);

        return new PublishContractVersionResult(version.Version, []);
    }

    private async Task<IReadOnlyList<InboundRouteSet>> LoadOtherActiveSetsAsync(
        ContractVersion version,
        CancellationToken cancellationToken)
    {
        var activeVersions = await db.ContractVersions
            .AsNoTracking()
            .Include(entity => entity.Channel)
            .Include(entity => entity.Operations)
            .Where(entity => entity.Status == ContractVersionStatus.Active && entity.ChannelId != version.ChannelId)
            .ToListAsync(cancellationToken);

        return activeVersions
            .Select(active => ContractRouteSets.Build(
                active.Id,
                active.Channel!.Name,
                active.Operations,
                ContractVersionSettings.From(active)))
            .ToArray();
    }

    private static IEnumerable<string> FindDuplicateOperationIds(
        IReadOnlyList<OpenApiOperationDefinition> operations) =>
        operations
            .Where(operation => operation.OperationId is not null)
            .GroupBy(operation => operation.OperationId!, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"operation_id: '{group.Key}' повторяется в документе");

    private static string Truncate(string value) => value.Length <= 2048 ? value : value[..2048];
}
