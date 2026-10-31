using LtiIntegrationGateway.Features.Inbound;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Primitives;

namespace LtiIntegrationGateway.Infrastructure.Routing;

/// <summary>
/// Источник endpoint'ов для входящих маршрутов из каталога. Перестраивает маршруты при изменении
/// каталога и сообщает об этом токеном <see cref="GetChangeToken"/>, поэтому активация и деактивация
/// маршрутов не требуют перезапуска приложения.
/// </summary>
public sealed class InboundEndpointDataSource(
    InboundRouteCatalog catalog,
    InboundRequestHandler requestHandler,
    InboundDisabledOperationHandler disabledOperationHandler) : EndpointDataSource
{
    private readonly object _gate = new();

    private IReadOnlyList<Endpoint> _endpoints = [];
    private CancellationToken _builtForToken;

    public override IReadOnlyList<Endpoint> Endpoints
    {
        get
        {
            lock (_gate)
            {
                var token = catalog.ChangeToken;

                if (_builtForToken != token)
                {
                    _endpoints = BuildEndpoints();
                    _builtForToken = token;
                }

                return _endpoints;
            }
        }
    }

    public override IChangeToken GetChangeToken() => new CancellationChangeToken(catalog.ChangeToken);

    private IReadOnlyList<Endpoint> BuildEndpoints() =>
        catalog.Snapshot().Select(BuildEndpoint).ToArray();

    private Endpoint BuildEndpoint(InboundRouteDefinition route)
    {
        // Отключённая операция не обслуживает вызовы: тот же маршрут, но обработчик отвечает 404 и
        // пишет запись аудита (ADR §7.3); конфликты маршрутов проверяются для всех операций.
        var handler = route.Enabled
            ? new RequestDelegate(requestHandler.HandleAsync)
            : disabledOperationHandler.HandleAsync;

        var builder = new RouteEndpointBuilder(
            handler,
            RoutePatternFactory.Parse(route.Template),
            order: 0);

        builder.Metadata.Add(new HttpMethodMetadata(new[] { route.HttpMethod.ToUpperInvariant() }));
        builder.Metadata.Add(new InboundChannelMetadata(route.ChannelName));
        builder.Metadata.Add(new InboundRouteMetadata(
            route.ChannelName,
            route.ContractVersionId,
            route.OperationId,
            route.RecipientQueue,
            route.AuthType,
            route.ConfigKey,
            route.HeaderName,
            route.UserName,
            route.RateLimitPermit,
            route.RateLimitWindowSeconds));
        builder.Metadata.Add(new EnableRateLimitingAttribute(InboundRateLimit.PolicyName));

        return builder.Build();
    }
}
