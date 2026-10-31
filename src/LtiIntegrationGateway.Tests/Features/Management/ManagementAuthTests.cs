using System.Net;
using Grpc.Core;
using LtiIntegrationGateway.Contracts.Management.V1;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace LtiIntegrationGateway.Tests.Features.Management;

public class ManagementAuthTests
{
    [Test]
    public async Task Rest_request_without_key_is_rejected()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var client = ManagementApi.CreateClient(factory, apiKey: null);

        var response = await client.GetAsync("/v1/channels");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(await response.Content.ReadAsStringAsync()).IsEmpty();
    }

    [Test]
    public async Task Rest_request_with_wrong_key_is_rejected()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var client = ManagementApi.CreateClient(factory, apiKey: "wrong-key");

        var response = await client.GetAsync("/v1/channels");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Management_api_without_configured_key_is_unavailable()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings(apiKey: null));
        using var client = ManagementApi.CreateClient(factory);

        var response = await client.GetAsync("/v1/channels");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
    }

    [Test]
    public async Task Rest_request_with_key_is_allowed()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var client = ManagementApi.CreateClient(factory);

        var response = await client.GetAsync("/v1/channels");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task Grpc_call_without_key_is_unauthenticated()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var channel = ManagementApi.CreateChannel(factory);
        var client = new ManagementService.ManagementServiceClient(channel);

        var exception = await Assert.ThrowsAsync<RpcException>(async () =>
            await client.ListChannelsAsync(new ListChannelsRequest(), ManagementApi.Call(apiKey: null)));

        await Assert.That(exception!.StatusCode).IsEqualTo(StatusCode.Unauthenticated);
    }

    [Test]
    public async Task Health_endpoint_does_not_require_key()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings(apiKey: null));
        using var client = ManagementApi.CreateClient(factory, apiKey: null);

        var response = await client.GetAsync("/health/live");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task Audit_does_not_contain_api_key_value()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var client = ManagementApi.CreateClient(factory);

        await client.GetAsync("/v1/channels");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
        var entries = db.ManagementAuditEntries.ToArray();

        await Assert.That(entries.Length).IsGreaterThanOrEqualTo(1);
        await Assert.That(entries.Any(entry =>
            (entry.Details ?? string.Empty).Contains(ManagementApi.ApiKey, StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task Channel_health_without_key_is_rejected()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var client = ManagementApi.CreateClient(factory, apiKey: null);

        var response = await client.GetAsync("/v1/channels/bank-a/health");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(await response.Content.ReadAsStringAsync()).IsEmpty();
    }

    [Test]
    public async Task Channel_health_without_configured_key_is_unavailable()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings(apiKey: null));
        using var client = ManagementApi.CreateClient(factory);

        var response = await client.GetAsync("/v1/channels/bank-a/health");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
    }
}
