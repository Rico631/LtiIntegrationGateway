using LtiIntegrationGateway.Infrastructure.OpenApi;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Contracts;
using LtiIntegrationGateway.Infrastructure.Routing;

namespace LtiIntegrationGateway.Features.Contracts;

/// <summary>Построение набора входящих маршрутов из операций версии контракта.</summary>
public static class ContractRouteSets
{
    public static string RevisionKey(Guid contractVersionId) => $"contract:{contractVersionId}";

    /// <summary>
    /// Строит набор маршрутов ревизии. Действующие настройки операции вычисляются здесь один раз,
    /// поэтому приём вызова не обращается к базе; отключённые операции остаются в наборе, чтобы
    /// конфликты «метод + шаблон» проверялись независимо от признака <c>enabled</c>.
    /// </summary>
    public static InboundRouteSet Build(
        Guid contractVersionId,
        string channelName,
        IEnumerable<ContractOperation> operations,
        ContractVersionSettings settings,
        InboundRateLimitOptions applicationDefaults)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(applicationDefaults);

        return new InboundRouteSet(
            RevisionKey(contractVersionId),
            operations
                .Select(operation => CreateRoute(channelName, contractVersionId, operation, settings, applicationDefaults))
                .ToArray());
    }

    public static InboundRouteSet Build(
        Guid contractVersionId,
        string channelName,
        IEnumerable<OpenApiOperationDefinition> operations) =>
        new(
            RevisionKey(contractVersionId),
            operations
                .Select(operation => new InboundRouteDefinition(channelName, operation.HttpMethod, operation.Template))
                .ToArray());

    private static InboundRouteDefinition CreateRoute(
        string channelName,
        Guid contractVersionId,
        ContractOperation operation,
        ContractVersionSettings settings,
        InboundRateLimitOptions applicationDefaults)
    {
        var effective = ContractOperationSettings.Resolve(operation, settings, applicationDefaults);

        return new InboundRouteDefinition(
            channelName,
            operation.HttpMethod,
            operation.Template,
            contractVersionId,
            operation.OperationId,
            effective.RecipientQueue,
            settings.AuthType,
            settings.ConfigKey,
            settings.HeaderName,
            settings.UserName,
            effective.RateLimitPermit,
            effective.RateLimitWindowSeconds,
            effective.Enabled);
    }
}


