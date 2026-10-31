using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace LtiIntegrationGateway.Features.Contracts;

/// <summary>Чтение операций версии контракта.</summary>
public sealed record ListContractOperationsQuery(string ChannelName, int Version);

/// <summary>Операция версии контракта вместе с её настройками.</summary>
public sealed record ContractOperationDetails(
    string HttpMethod,
    string Template,
    string? OperationId,
    bool Enabled,
    string? RecipientQueue,
    int? RateLimitPermit,
    int? RateLimitWindowSeconds);

public sealed record ListContractOperationsResult(
    IReadOnlyList<ContractOperationDetails> Operations,
    IReadOnlyList<string> Problems);

public sealed class ListContractOperationsHandler(GatewayDbContext db)
{
    public async Task<ListContractOperationsResult> HandleAsync(
        ListContractOperationsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var version = await db.ContractVersions
            .AsNoTracking()
            .Include(entity => entity.Operations)
            .Where(entity => entity.Channel!.Name == query.ChannelName && entity.Version == query.Version)
            .SingleOrDefaultAsync(cancellationToken);

        if (version is null)
        {
            return new ListContractOperationsResult([], ["version: версия контракта не найдена"]);
        }

        var operations = version.Operations
            .OrderBy(operation => operation.HttpMethod, StringComparer.Ordinal)
            .ThenBy(operation => operation.Template, StringComparer.Ordinal)
            .Select(operation => new ContractOperationDetails(
                operation.HttpMethod,
                operation.Template,
                operation.OperationId,
                operation.Enabled,
                operation.RecipientQueue,
                operation.RateLimitPermit,
                operation.RateLimitWindowSeconds))
            .ToArray();

        return new ListContractOperationsResult(operations, []);
    }
}
