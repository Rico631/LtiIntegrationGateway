using LtiIntegrationGateway.Features.Contracts;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities;
using LtiIntegrationGateway.Infrastructure.Routing;
using LtiIntegrationGateway.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace LtiIntegrationGateway.Tests.Features.Contracts;

public class ContractOperationSettingsTests
{
    private static readonly Guid VersionId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly InboundRateLimitOptions ApplicationDefaults = new()
    {
        PermitLimit = 100,
        WindowSeconds = 60,
    };

    private static ContractVersionSettings VersionSettings(
        string? queue = "version.queue",
        int? permit = 10,
        int? window = 30) =>
        new(queue, ChannelAuthType.ApiKey, "bank-a.inbound", "X-Bank-Key", null, permit, window);

    private static ContractOperation Operation(
        bool enabled = true,
        string? queue = null,
        int? permit = null,
        int? window = null,
        string method = "POST",
        string template = "/callbacks/contracts",
        string? operationId = "receiveContract") => new()
    {
        Id = Guid.NewGuid(),
        ContractVersionId = VersionId,
        HttpMethod = method,
        Template = template,
        OperationId = operationId,
        Enabled = enabled,
        RecipientQueue = queue,
        RateLimitPermit = permit,
        RateLimitWindowSeconds = window,
    };

    [Test]
    public async Task Operation_queue_and_quota_override_version_and_application()
    {
        var effective = ContractOperationSettings.Resolve(
            Operation(queue: "operation.queue", permit: 5, window: 15),
            VersionSettings(),
            ApplicationDefaults);

        await Assert.That(effective.Enabled).IsTrue();
        await Assert.That(effective.RecipientQueue).IsEqualTo("operation.queue");
        await Assert.That(effective.RateLimitPermit).IsEqualTo(5);
        await Assert.That(effective.RateLimitWindowSeconds).IsEqualTo(15);
    }

    [Test]
    public async Task Version_settings_apply_when_operation_has_no_overrides()
    {
        var effective = ContractOperationSettings.Resolve(
            Operation(queue: "   "),
            VersionSettings(),
            ApplicationDefaults);

        await Assert.That(effective.RecipientQueue).IsEqualTo("version.queue");
        await Assert.That(effective.RateLimitPermit).IsEqualTo(10);
        await Assert.That(effective.RateLimitWindowSeconds).IsEqualTo(30);
    }

    [Test]
    public async Task Application_defaults_apply_when_neither_operation_nor_version_set_quota()
    {
        var effective = ContractOperationSettings.Resolve(
            Operation(),
            VersionSettings(queue: null, permit: null, window: null),
            ApplicationDefaults);

        await Assert.That(effective.RecipientQueue).IsNull();
        await Assert.That(effective.RateLimitPermit).IsEqualTo(ApplicationDefaults.PermitLimit);
        await Assert.That(effective.RateLimitWindowSeconds).IsEqualTo(ApplicationDefaults.WindowSeconds);
    }

    [Test]
    public async Task Route_set_contains_enabled_and_disabled_operations_with_effective_settings()
    {
        var set = ContractRouteSets.Build(
            VersionId,
            "bank-a",
            [
                Operation(queue: "operation.queue", permit: 5, window: 15),
                Operation(method: "GET", template: "/callbacks/orders", operationId: "listOrders", enabled: false),
            ],
            VersionSettings(),
            ApplicationDefaults);

        await Assert.That(set.Routes.Count).IsEqualTo(2);

        var enabled = set.Routes.Single(route => route.OperationId == "receiveContract");

        await Assert.That(enabled.Enabled).IsTrue();
        await Assert.That(enabled.RecipientQueue).IsEqualTo("operation.queue");
        await Assert.That(enabled.RateLimitPermit).IsEqualTo(5);
        await Assert.That(enabled.RateLimitWindowSeconds).IsEqualTo(15);

        var disabled = set.Routes.Single(route => route.OperationId == "listOrders");

        await Assert.That(disabled.Enabled).IsFalse();
        await Assert.That(disabled.RecipientQueue).IsEqualTo("version.queue");
        await Assert.That(disabled.RateLimitPermit).IsEqualTo(10);
        await Assert.That(disabled.AuthType).IsEqualTo(ChannelAuthType.ApiKey);
    }

    [Test]
    public async Task Disabled_operation_conflicts_are_reported()
    {
        var candidate = ContractRouteSets.Build(
            VersionId,
            "bank-a",
            [Operation(method: "GET", template: "/callbacks/orders", operationId: "listOrders", enabled: false)],
            VersionSettings(),
            ApplicationDefaults);

        var active = new InboundRouteSet(
            "contract:other",
            [new InboundRouteDefinition("bank-b", "GET", "/callbacks/orders")]);

        var problems = new InboundRouteSetValidator().Validate(candidate, [active]);

        await Assert.That(problems.Any(problem => problem.StartsWith("conflict:", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Disabled_operation_is_persisted_with_explicit_false()
    {
        using var database = new SqliteInMemoryDatabase();
        database.Migrate();

        var versionId = Guid.NewGuid();

        await using (var context = database.CreateContext())
        {
            var channel = new Channel { Id = Guid.NewGuid(), Name = "bank-a", CreatedAt = DateTimeOffset.UtcNow };

            context.Channels.Add(channel);
            context.ContractVersions.Add(new ContractVersion
            {
                Id = versionId,
                ChannelId = channel.Id,
                Version = 1,
                SourceFormat = ContractSourceFormat.OpenApi30,
                Document = "{}",
                Status = ContractVersionStatus.Draft,
                CreatedAt = DateTimeOffset.UtcNow,
                Operations =
                {
                    new ContractOperation
                    {
                        Id = Guid.NewGuid(),
                        ContractVersionId = versionId,
                        HttpMethod = "POST",
                        Template = "/callbacks/contracts",
                        OperationId = "receiveContract",
                        Enabled = false,
                    },
                },
            });

            await context.SaveChangesAsync();
        }

        await using (var context = database.CreateContext())
        {
            var operation = await context.ContractOperations.AsNoTracking().SingleAsync();

            await Assert.That(operation.Enabled).IsFalse();
        }
    }
}
