using LtiIntegrationGateway.Tests.TestSupport;

namespace LtiIntegrationGateway.Tests.Features.Host;

public class StartupLoggingTests
{
    [Test]
    public async Task Startup_logs_do_not_contain_connection_string_value()
    {
        using var factory = ManagementHostFactory.WithStorage();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");
        response.EnsureSuccessStatusCode();

        var leaked = factory.Logging.Entries
            .Where(entry => entry.Message.Contains("gateway-tests-", StringComparison.Ordinal))
            .Select(entry => entry.Message)
            .ToArray();

        await Assert.That(leaked.Length).IsEqualTo(0);
    }
}
