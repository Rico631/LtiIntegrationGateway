using System.Net;
using LtiIntegrationGateway.Tests.TestSupport;

namespace LtiIntegrationGateway.Tests.Features.Host;

public class LivenessTests
{
    [Test]
    public async Task Liveness_returns_ok_when_no_external_dependencies_are_configured()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();

        await Assert.That(body).IsEqualTo("Healthy");
    }
}
