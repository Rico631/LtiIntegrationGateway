using LtiIntegrationGateway.Infrastructure.Management;
using LtiIntegrationGateway.Infrastructure.OpenApi;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities;
using LtiIntegrationGateway.Infrastructure.Routing;

namespace LtiIntegrationGateway.Features.Contracts;

/// <summary>Построение набора входящих маршрутов из операций версии контракта.</summary>
internal static class ContractRouteSets
{
    public static string RevisionKey(Guid contractVersionId) => $"contract:{contractVersionId}";

    public static InboundRouteSet Build(
        Guid contractVersionId,
        string channelName,
        IEnumerable<ContractOperation> operations,
        ContractVersionSettings settings) =>
        new(
            RevisionKey(contractVersionId),
            operations
                .Select(operation => new InboundRouteDefinition(
                    channelName,
                    operation.HttpMethod,
                    operation.Template,
                    contractVersionId,
                    operation.OperationId,
                    settings.RecipientQueue,
                    settings.AuthType,
                    settings.ConfigKey,
                    settings.HeaderName,
                    settings.UserName,
                    settings.RateLimitPermit,
                    settings.RateLimitWindowSeconds))
                .ToArray());

    public static InboundRouteSet Build(
        Guid contractVersionId,
        string channelName,
        IEnumerable<OpenApiOperationDefinition> operations) =>
        new(
            RevisionKey(contractVersionId),
            operations
                .Select(operation => new InboundRouteDefinition(channelName, operation.HttpMethod, operation.Template))
                .ToArray());
}


