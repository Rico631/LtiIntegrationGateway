using System.Net;
using LtiIntegrationGateway.Infrastructure.Routing;
using LtiIntegrationGateway.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace LtiIntegrationGateway.Tests.Features.Inbound;

public class InboundRateLimitTests
{
    private static readonly Dictionary<string, string?> TwoRequestWindow = new()
    {
        ["Inbound:RateLimit:PermitLimit"] = "2",
        ["Inbound:RateLimit:WindowSeconds"] = "60",
    };

    private static InboundRouteSet Set(
        string revisionKey,
        params (string Channel, string Method, string Template)[] routes) =>
        new(revisionKey, routes
            .Select(route => new InboundRouteDefinition(route.Channel, route.Method, route.Template))
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
            .IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await client.GetAsync("/callbacks/bank-a")).StatusCode)
            .IsEqualTo(HttpStatusCode.OK);

        var overQuota = await client.GetAsync("/callbacks/bank-a");

        await Assert.That(overQuota.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);

        var otherChannel = await client.GetAsync("/callbacks/bank-b");

        await Assert.That(otherChannel.StatusCode).IsEqualTo(HttpStatusCode.OK);
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
            .IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await client.GetAsync("/callbacks/window")).StatusCode)
            .IsEqualTo(HttpStatusCode.TooManyRequests);

        await Task.Delay(TimeSpan.FromMilliseconds(1200));

        await Assert.That((await client.GetAsync("/callbacks/window")).StatusCode)
            .IsEqualTo(HttpStatusCode.OK);
    }
}
