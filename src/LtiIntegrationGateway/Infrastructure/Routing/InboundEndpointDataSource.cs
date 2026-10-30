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
    InboundProbeHandler probeHandler) : EndpointDataSource
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
        var builder = new RouteEndpointBuilder(
            probeHandler.HandleAsync,
            RoutePatternFactory.Parse(route.Template),
            order: 0);

        builder.Metadata.Add(new HttpMethodMetadata(new[] { route.HttpMethod.ToUpperInvariant() }));
        builder.Metadata.Add(new InboundChannelMetadata(route.ChannelName));
        builder.Metadata.Add(new EnableRateLimitingAttribute(InboundRateLimit.PolicyName));

        return builder.Build();
    }
}
