using LtiIntegrationGateway.Features.Contracts.ListContractOperations;
using LtiIntegrationGateway.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LtiIntegrationGateway.Features.Contracts.GetContractVersion;

/// <summary>Чтение версии контракта вместе с настройками и операциями.</summary>
public sealed record GetContractVersionQuery(string ChannelName, int Version);

public sealed record ContractVersionDetails(
    int Version,
    string Status,
    ContractVersionSettings Settings,
    IReadOnlyList<ContractOperationDetails> Operations);

public sealed record GetContractVersionResult(ContractVersionDetails? ContractVersion, IReadOnlyList<string> Problems);

public sealed class GetContractVersionHandler(GatewayDbContext db)
{
    public async Task<GetContractVersionResult> HandleAsync(
        GetContractVersionQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var version = await db.ContractVersions
            .AsNoTracking()
            .Include(entity => entity.Operations)
            .Where(entity => entity.InboundChannel!.Name == query.ChannelName && entity.Version == query.Version)
            .SingleOrDefaultAsync(cancellationToken);

        if (version is null)
        {
            return new GetContractVersionResult(null, ["version: версия контракта не найдена"]);
        }

        var details = new ContractVersionDetails(
            version.Version,
            version.Status.ToString(),
            new ContractVersionSettings(
                version.RecipientQueue,
                version.AuthType,
                version.ConfigKey,
                version.HeaderName,
                version.UserName,
                version.RateLimitPermit,
                version.RateLimitWindowSeconds),
            version.Operations
                .OrderBy(operation => operation.HttpMethod, StringComparer.Ordinal)
                .ThenBy(operation => operation.Template, StringComparer.Ordinal)
                .Select(operation => new ContractOperationDetails(
                    operation.HttpMethod,
                    operation.Template,
                    operation.OperationId,
                    operation.Enabled,
                    operation.RecipientQueue))
                .ToArray());

        return new GetContractVersionResult(details, []);
    }
}
