using System.Net;
using LtiIntegrationGateway.Features.Contracts;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities;
using LtiIntegrationGateway.Infrastructure.Routing;
using LtiIntegrationGateway.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace LtiIntegrationGateway.Tests.Features.Inbound;

public class InboundRateLimitTests
{
    private const string RecipientQueue = "dpv.inbox";

    private static readonly Dictionary<string, string?> TwoRequestWindow = new()
    {
        ["Inbound:RateLimit:PermitLimit"] = "2",
        ["Inbound:RateLimit:WindowSeconds"] = "60",
    };

    private static InboundRouteSet Set(
        string revisionKey,
        params (string Channel, string Method, string Template)[] routes) =>
        new(revisionKey, routes
            .Select(route => new InboundRouteDefinition(
                route.Channel,
                route.Method,
                route.Template,
                RecipientQueue: RecipientQueue))
            .ToArray());

    private static InboundRouteSet SetWithQuota(
        string revisionKey,
        int permitLimit,
        int windowSeconds,
        params (string Channel, string Method, string Template)[] routes) =>
        new(revisionKey, routes
            .Select(route => new InboundRouteDefinition(
                route.Channel,
                route.Method,
                route.Template,
                RecipientQueue: RecipientQueue,
                RateLimitPermit: permitLimit,
                RateLimitWindowSeconds: windowSeconds))
            .ToArray());

    private static InboundRouteCatalog CatalogOf(GatewayWebApplicationFactory factory) =>
        factory.Services.GetRequiredService<InboundRouteCatalog>();

    [Test]
    public async Task Quota_is_enforced_per_channel_and_channels_are_independent()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(TwoRequestWindow);
        using var client = factory.CreateClient();

        CatalogOf(factory).Activate(Set(
            "rev-1",
            ("bank-a", "GET", "/callbacks/bank-a"),
            ("bank-b", "GET", "/callbacks/bank-b")));

        await Assert.That((await client.GetAsync("/callbacks/bank-a")).StatusCode)
            .IsEqualTo(HttpStatusCode.Accepted);
        await Assert.That((await client.GetAsync("/callbacks/bank-a")).StatusCode)
            .IsEqualTo(HttpStatusCode.Accepted);

        var overQuota = await client.GetAsync("/callbacks/bank-a");

        await Assert.That(overQuota.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);

        var otherChannel = await client.GetAsync("/callbacks/bank-b");

        await Assert.That(otherChannel.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
    }

    [Test]
    public async Task Quota_is_restored_after_window()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(new Dictionary<string, string?>
        {
            ["Inbound:RateLimit:PermitLimit"] = "1",
            ["Inbound:RateLimit:WindowSeconds"] = "1",
        });

        using var client = factory.CreateClient();

        CatalogOf(factory).Activate(Set("rev-1", ("bank-a", "GET", "/callbacks/window")));

        await Assert.That((await client.GetAsync("/callbacks/window")).StatusCode)
            .IsEqualTo(HttpStatusCode.Accepted);
        await Assert.That((await client.GetAsync("/callbacks/window")).StatusCode)
            .IsEqualTo(HttpStatusCode.TooManyRequests);

        await Task.Delay(TimeSpan.FromMilliseconds(1200));

        await Assert.That((await client.GetAsync("/callbacks/window")).StatusCode)
            .IsEqualTo(HttpStatusCode.Accepted);
    }

    [Test]
    public async Task Contract_settings_override_application_quota()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage();

        using var client = factory.CreateClient();

        CatalogOf(factory).Activate(SetWithQuota(
            "rev-1",
            permitLimit: 1,
            windowSeconds: 60,
            ("bank-a", "GET", "/callbacks/limited")));

        await Assert.That((await client.GetAsync("/callbacks/limited")).StatusCode)
            .IsEqualTo(HttpStatusCode.Accepted);
        await Assert.That((await client.GetAsync("/callbacks/limited")).StatusCode)
            .IsEqualTo(HttpStatusCode.TooManyRequests);
    }

    [Test]
    public async Task Route_without_quota_uses_application_defaults()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage();

        using var client = factory.CreateClient();

        CatalogOf(factory).Activate(Set("rev-1", ("bank-a", "GET", "/callbacks/defaults")));

        await Assert.That((await client.GetAsync("/callbacks/defaults")).StatusCode)
            .IsEqualTo(HttpStatusCode.Accepted);
        await Assert.That((await client.GetAsync("/callbacks/defaults")).StatusCode)
            .IsEqualTo(HttpStatusCode.Accepted);
    }

    [Test]
    public async Task Operations_share_channel_quota()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage();

        using var client = factory.CreateClient();

        var versionSettings = new ContractVersionSettings(
            "dpv.inbox",
            ChannelAuthType.None,
            null,
            null,
            null,
            RateLimitPermit: 1,
            RateLimitWindowSeconds: 60);

        var operations = new[]
        {
            new ContractOperation
            {
                Id = Guid.NewGuid(),
                ContractVersionId = Guid.Empty,
                HttpMethod = "GET",
                Template = "/callbacks/limited",
                OperationId = "limited",
            },
            new ContractOperation
            {
                Id = Guid.NewGuid(),
                ContractVersionId = Guid.Empty,
                HttpMethod = "GET",
                Template = "/callbacks/open",
                OperationId = "open",
            },
        };

        var set = ContractRouteSets.Build(
            Guid.NewGuid(),
            "bank-a",
            operations,
            versionSettings,
            new InboundRateLimitOptions { PermitLimit = 100, WindowSeconds = 60 });

        CatalogOf(factory).Activate(set);

        await Assert.That((await client.GetAsync("/callbacks/limited")).StatusCode)
            .IsEqualTo(HttpStatusCode.Accepted);
        await Assert.That((await client.GetAsync("/callbacks/open")).StatusCode)
            .IsEqualTo(HttpStatusCode.TooManyRequests);
    }

    [Test]
    public async Task Route_metadata_carries_effective_operation_settings()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage();

        var versionSettings = new ContractVersionSettings(
            "dpv.inbox",
            ChannelAuthType.ApiKey,
            "bank-a.inbound",
            "X-Bank-Key",
            null,
            RateLimitPermit: 100,
            RateLimitWindowSeconds: 60);

        var set = ContractRouteSets.Build(
            Guid.NewGuid(),
            "bank-a",
            [
                new ContractOperation
                {
                    Id = Guid.NewGuid(),
                    ContractVersionId = Guid.Empty,
                    HttpMethod = "GET",
                    Template = "/callbacks/operation",
                    OperationId = "operation",
                    RecipientQueue = "other.inbox",
                },
            ],
            versionSettings,
            new InboundRateLimitOptions { PermitLimit = 1, WindowSeconds = 1 });

        CatalogOf(factory).Activate(set);

        var endpoint = factory.Services.GetRequiredService<InboundEndpointDataSource>().Endpoints.Single();
        var metadata = endpoint.Metadata.GetMetadata<InboundRouteMetadata>();

        await Assert.That(metadata).IsNotNull();
        await Assert.That(metadata!.ChannelName).IsEqualTo("bank-a");
        await Assert.That(metadata.RecipientQueue).IsEqualTo("other.inbox");
        await Assert.That(metadata.RateLimitPermit).IsEqualTo(100);
        await Assert.That(metadata.RateLimitWindowSeconds).IsEqualTo(60);
        await Assert.That(metadata.AuthType).IsEqualTo(ChannelAuthType.ApiKey);
    }
}
