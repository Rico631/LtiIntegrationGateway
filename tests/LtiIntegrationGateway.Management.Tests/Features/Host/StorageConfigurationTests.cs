using System.Net;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Tests.TestSupport;
using Npgsql;

namespace LtiIntegrationGateway.Tests.Features.Host;

public class StorageConfigurationTests
{
    [Test]
    public async Task Authentication_failure_is_detected_through_wrappers()
    {
        var postgres = new PostgresException(
            "password authentication failed",
            "FATAL",
            "FATAL",
            PostgresErrorCodes.InvalidPassword);

        await Assert.That(GatewayStorageExtensions.IsAuthenticationFailure(postgres)).IsTrue();
        await Assert.That(GatewayStorageExtensions.IsAuthenticationFailure(
            new NpgsqlException("connection failed", postgres))).IsTrue();
        await Assert.That(GatewayStorageExtensions.IsAuthenticationFailure(
            new InvalidOperationException("other"))).IsFalse();
        await Assert.That(GatewayStorageExtensions.IsAuthenticationFailure(null)).IsFalse();
    }

    [Test]
    public async Task Startup_fails_when_connection_string_is_missing()
    {
        Exception? captured = null;

        try
        {
            using var factory = ManagementHostFactory.WithoutStorage();
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
        using var factory = ManagementHostFactory.WithStorage();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }
}
