using System.Net;
using LtiIntegrationGateway.Tests.TestSupport;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LtiIntegrationGateway.Tests.Features.Host;

/// <summary>Проверка readiness-эндпоинта хоста.</summary>
public class ReadinessTests
{
    [Test]
    public async Task Readiness_returns_ok_when_storage_is_available()
    {
        using var factory = ManagementHostFactory.WithStorage();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();

        await Assert.That(body).IsEqualTo("Healthy");
    }

    [Test]
    public async Task Readiness_returns_service_unavailable_when_a_ready_check_fails()
    {
        using var factory = ManagementHostFactory.WithUnhealthyReadiness(new UnhealthyHealthCheck());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
    }

    [Test]
    public async Task Liveness_stays_ok_when_readiness_fails()
    {
        using var factory = ManagementHostFactory.WithUnhealthyReadiness(new UnhealthyHealthCheck());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    private sealed class UnhealthyHealthCheck : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(HealthCheckResult.Unhealthy("unavailable"));
    }
}
