using System.Net;
using LtiIntegrationGateway.Tests.TestSupport;

namespace LtiIntegrationGateway.Tests.Features.Host;

public class StorageConfigurationTests
{
    [Test]
    public async Task Startup_fails_when_connection_string_is_missing()
    {
        Exception? captured = null;

        try
        {
            using var factory = GatewayWebApplicationFactory.WithoutStorage();
            using var client = factory.CreateClient();
        }
        catch (Exception exception)
        {
            captured = exception;
        }

        await Assert.That(captured is not null).IsTrue();
        await Assert
            .That(captured!.ToString().Contains("ConnectionStrings:Gateway", StringComparison.Ordinal))
            .IsTrue();
    }

    [Test]
    public async Task Startup_succeeds_when_connection_string_is_provided()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }
}
