using System.Net;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities;
using LtiIntegrationGateway.Infrastructure.Routing;
using LtiIntegrationGateway.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LtiIntegrationGateway.Tests.Features.Inbound;

public class InboundRoutingTests
{
    private const string RecipientQueue = "product.inbound";

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

    private static InboundRouteCatalog CatalogOf(GatewayWebApplicationFactory factory) =>
        factory.Services.GetRequiredService<InboundRouteCatalog>();

    private static async Task<IReadOnlyList<InboundMessageLog>> LogsAsync(GatewayWebApplicationFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();

        return (await db.InboundMessageLogs.AsNoTracking().ToArrayAsync())
            .OrderBy(entry => entry.ReceivedAt)
            .ToArray();
    }

    [Test]
    public async Task Route_activated_after_start_serves_requests_without_restart()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage();
        using var client = factory.CreateClient();

        var before = await client.GetAsync("/callbacks/contracts");

        await Assert.That(before.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

        var activation = CatalogOf(factory).Activate(Set("rev-1", ("bank-a", "GET", "/callbacks/contracts")));

        await Assert.That(activation.Succeeded).IsTrue();

        var after = await client.GetAsync("/callbacks/contracts");

        await Assert.That(after.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
    }

    [Test]
    public async Task Deactivated_route_returns_not_found()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage();
        using var client = factory.CreateClient();

        var catalog = CatalogOf(factory);
        catalog.Activate(Set("rev-1", ("bank-a", "GET", "/callbacks/orders")));

        await Assert.That((await client.GetAsync("/callbacks/orders")).StatusCode)
            .IsEqualTo(HttpStatusCode.Accepted);

        catalog.Deactivate("rev-1");

        await Assert.That((await client.GetAsync("/callbacks/orders")).StatusCode)
            .IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Unknown_path_returns_not_found()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage();
        using var client = factory.CreateClient();

        CatalogOf(factory).Activate(Set("rev-1", ("bank-a", "GET", "/callbacks/orders")));

        var response = await client.GetAsync("/callbacks/unknown");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Accepted_call_is_journaled_with_selected_route()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage();
        using var client = factory.CreateClient();

        CatalogOf(factory).Activate(Set("rev-1", ("bank-a", "PATCH", "/callbacks/contracts/{id}/status")));

        var response = await client.PatchAsync("/callbacks/contracts/42/status?source=crm", null);

        var logs = await LogsAsync(factory);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        await Assert.That(logs.Count).IsEqualTo(1);
        await Assert.That(logs[0].HttpMethod).IsEqualTo("PATCH");
        await Assert.That(logs[0].RouteTemplate).IsEqualTo("/callbacks/contracts/{id}/status");
        await Assert.That(logs[0].RelativePath).IsEqualTo("/callbacks/contracts/42/status");
        await Assert.That(logs[0].ChannelName).IsEqualTo("bank-a");
        await Assert.That(logs[0].DeliveryStatus).IsEqualTo(InboundDeliveryStatus.Sent);
    }

    [Test]
    public async Task Conflicting_set_is_rejected_and_active_set_keeps_serving()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage();
        using var client = factory.CreateClient();

        var catalog = CatalogOf(factory);
        catalog.Activate(Set("rev-1", ("bank-a", "GET", "/callbacks/orders/{id}")));

        var conflicting = catalog.Activate(Set("rev-2", ("bank-b", "GET", "/callbacks/orders/{id}")));

        await Assert.That(conflicting.Succeeded).IsFalse();
        await Assert.That(catalog.ActiveRevisionKeys).IsEquivalentTo(new[] { "rev-1" });

        await Assert.That((await client.GetAsync("/callbacks/orders/17")).StatusCode)
            .IsEqualTo(HttpStatusCode.Accepted);

        var logs = await LogsAsync(factory);

        await Assert.That(logs.Single().ChannelName).IsEqualTo("bank-a");
    }

    [Test]
    public async Task Set_with_internal_conflict_is_rejected_then_fixed_set_activates()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage();
        using var client = factory.CreateClient();

        var catalog = CatalogOf(factory);

        var rejected = catalog.Activate(Set(
            "rev-1",
            ("bank-a", "POST", "/callbacks/contracts"),
            ("bank-a", "POST", "/callbacks/contracts")));

        await Assert.That(rejected.Succeeded).IsFalse();
        await Assert.That(catalog.Snapshot().Count).IsEqualTo(0);
        await Assert.That((await client.PostAsync("/callbacks/contracts", null)).StatusCode)
            .IsEqualTo(HttpStatusCode.NotFound);

        var activation = catalog.Activate(Set("rev-2", ("bank-a", "POST", "/callbacks/contracts")));

        await Assert.That(activation.Succeeded).IsTrue();
        await Assert.That((await client.PostAsync("/callbacks/contracts", null)).StatusCode)
            .IsEqualTo(HttpStatusCode.Accepted);
    }

    [Test]
    public async Task Literal_segment_wins_over_parameter()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage();
        using var client = factory.CreateClient();

        CatalogOf(factory).Activate(Set(
            "rev-1",
            ("bank-a", "GET", "/callbacks/{name}"),
            ("bank-a", "GET", "/callbacks/health")));

        var response = await client.GetAsync("/callbacks/health");

        var logs = await LogsAsync(factory);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        await Assert.That(logs.Single().RouteTemplate).IsEqualTo("/callbacks/health");
    }

    [Test]
    public async Task Parameter_route_serves_other_values_and_selection_is_stable()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage();
        using var client = factory.CreateClient();

        CatalogOf(factory).Activate(Set(
            "rev-1",
            ("bank-a", "GET", "/callbacks/{name}"),
            ("bank-a", "GET", "/callbacks/health")));

        await client.GetAsync("/callbacks/payments");
        await client.GetAsync("/callbacks/payments");

        var logs = await LogsAsync(factory);

        await Assert.That(logs.Count).IsEqualTo(2);
        await Assert.That(logs.All(entry => entry.RouteTemplate == "/callbacks/{name}")).IsTrue();
        await Assert.That(logs.All(entry => entry.RelativePath == "/callbacks/payments")).IsTrue();
    }

    [Test]
    public async Task Static_health_endpoint_is_not_affected_by_dynamic_routes()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage();
        using var client = factory.CreateClient();

        CatalogOf(factory).Activate(Set("rev-1", ("bank-a", "GET", "/health")));

        var response = await client.GetAsync("/health/live");
        var body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(body).IsEqualTo("Healthy");
    }

    [Test]
    public async Task Method_mismatch_on_two_segment_path_is_answered_by_platform_pattern()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage();
        using var client = factory.CreateClient();

        CatalogOf(factory).Activate(Set("rev-1", ("bank-a", "GET", "/callbacks/orders")));

        var response = await client.PostAsync("/callbacks/orders", null);

        // Платформенный шаблон gRPC-сервисов /{service}/{method} тоже совпадает с двумя сегментами и
        // перехватывает запрос с недопустимым методом, поэтому ответ 404, а не 405.
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Method_mismatch_on_three_segment_path_returns_method_not_allowed()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage();
        using var client = factory.CreateClient();

        CatalogOf(factory).Activate(Set("rev-1", ("bank-a", "GET", "/callbacks/orders/{id}")));

        var response = await client.PostAsync("/callbacks/orders/42", null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.MethodNotAllowed);
    }
}
