using LtiIntegrationGateway.Infrastructure.Routing;

namespace LtiIntegrationGateway.Tests.Infrastructure.Routing;

public class InboundRouteCatalogTests
{
    private static InboundRouteSet Set(string revisionKey, params (string Channel, string Method, string Template)[] routes) =>
        new(revisionKey, routes.Select(route => new InboundRouteDefinition(route.Channel, route.Method, route.Template)).ToArray());

    [Test]
    public async Task Activation_exposes_routes_in_snapshot()
    {
        var catalog = new InboundRouteCatalog();

        var result = catalog.Activate(Set(
            "rev-1",
            ("bank-a", "GET", "/callbacks/contracts"),
            ("bank-a", "POST", "/callbacks/contracts")));

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(catalog.Snapshot().Count).IsEqualTo(2);
        await Assert.That(catalog.ActiveRevisionKeys).IsEquivalentTo(new[] { "rev-1" });
    }

    [Test]
    public async Task Deactivation_removes_routes_of_the_set()
    {
        var catalog = new InboundRouteCatalog();
        catalog.Activate(Set("rev-1", ("bank-a", "GET", "/callbacks/contracts")));

        var removed = catalog.Deactivate("rev-1");

        await Assert.That(removed).IsTrue();
        await Assert.That(catalog.Snapshot().Count).IsEqualTo(0);
        await Assert.That(catalog.Deactivate("rev-1")).IsFalse();
    }

    [Test]
    public async Task Duplicate_route_within_set_is_rejected()
    {
        var catalog = new InboundRouteCatalog();

        var result = catalog.Activate(Set(
            "rev-1",
            ("bank-a", "POST", "/callbacks/contracts"),
            ("bank-a", "POST", "/callbacks/contracts")));

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Problems.Any(problem => problem.Contains("conflict:", StringComparison.Ordinal))).IsTrue();
        await Assert.That(catalog.Snapshot().Count).IsEqualTo(0);
    }

    [Test]
    public async Task Route_conflicting_with_active_set_is_rejected_and_active_set_survives()
    {
        var catalog = new InboundRouteCatalog();
        catalog.Activate(Set("rev-1", ("bank-a", "GET", "/callbacks/orders/{id}")));

        var result = catalog.Activate(Set("rev-2", ("bank-b", "GET", "/callbacks/orders/{id}")));

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(catalog.ActiveRevisionKeys).IsEquivalentTo(new[] { "rev-1" });
        await Assert.That(catalog.Snapshot().Single().ChannelName).IsEqualTo("bank-a");
    }

    [Test]
    public async Task Different_parameter_names_are_treated_as_conflict()
    {
        var catalog = new InboundRouteCatalog();
        catalog.Activate(Set("rev-1", ("bank-a", "GET", "/callbacks/{id}")));

        var result = catalog.Activate(Set("rev-2", ("bank-b", "GET", "/callbacks/{name}")));

        await Assert.That(result.Succeeded).IsFalse();
    }

    [Test]
    public async Task Unsupported_method_is_rejected()
    {
        var catalog = new InboundRouteCatalog();

        var result = catalog.Activate(Set("rev-1", ("bank-a", "TRACE", "/callbacks/contracts")));

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Problems.Any(problem => problem.StartsWith("method:", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Invalid_template_is_rejected()
    {
        var catalog = new InboundRouteCatalog();

        var result = catalog.Activate(Set("rev-1", ("bank-a", "GET", "/callbacks/{id")));

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Problems.Any(problem => problem.StartsWith("template:", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Change_token_is_cancelled_on_activation_and_deactivation()
    {
        var catalog = new InboundRouteCatalog();
        var beforeActivation = catalog.ChangeToken;

        catalog.Activate(Set("rev-1", ("bank-a", "GET", "/callbacks/contracts")));

        var afterActivation = catalog.ChangeToken;
        catalog.Deactivate("rev-1");

        await Assert.That(beforeActivation.IsCancellationRequested).IsTrue();
        await Assert.That(afterActivation.IsCancellationRequested).IsTrue();
        await Assert.That(catalog.ChangeToken.IsCancellationRequested).IsFalse();
    }
}
