using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;

namespace LtiIntegrationGateway.Infrastructure.Health;

/// <summary>Пробы живучести и готовности, одинаковые для сервиса управления и сервиса потоков.</summary>
public static class GatewayHealthEndpointExtensions
{
    /// <summary>Публикует <c>/health/live</c> без проверок и <c>/health/ready</c> по тегу <c>ready</c>.</summary>
    public static IEndpointRouteBuilder MapGatewayHealthProbes(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        endpoints.MapHealthChecks(
            "/health/ready",
            new HealthCheckOptions { Predicate = registration => registration.Tags.Contains("ready") });

        return endpoints;
    }
}
