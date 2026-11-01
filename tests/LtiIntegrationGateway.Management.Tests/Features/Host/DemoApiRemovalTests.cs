using System.Net;
using LtiIntegrationGateway.Tests.TestSupport;

namespace LtiIntegrationGateway.Tests.Features.Host;

public class DemoApiRemovalTests
{
    [Test]
    public async Task WeatherForecast_endpoint_is_not_published()
    {
        using var factory = ManagementHostFactory.WithStorage();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/weatherforecast");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }
}
