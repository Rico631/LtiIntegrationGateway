using LtiIntegrationGateway.Features.Configuration;
using LtiIntegrationGateway.Features.Outbound;
using LtiIntegrationGateway.Infrastructure.Observability;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Outbound;
using LtiIntegrationGateway.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LtiIntegrationGateway.Tests.Features.Outbound;

/// <summary>Проверка наблюдаемого gauge ожидающих доставки результатов.</summary>
[NotInParallel]
public class OutboundBacklogMetricsUpdaterTests
{
    [Test]
    public async Task Gauge_counts_only_pending_logs_with_address_and_operation()
    {
        await using var fixture = new OutboundFixture();
        var createdAt = DateTimeOffset.UtcNow;

        await AddAsync(fixture, "deliverable", "dpv.inbox", withOperation: true, createdAt);
        await AddAsync(fixture, "without-address", string.Empty, withOperation: true, createdAt);
        await AddAsync(fixture, "without-operation", "dpv.inbox", withOperation: false, createdAt);
        await AddAsync(
            fixture,
            "already-sent",
            "dpv.inbox",
            withOperation: true,
            createdAt,
            OutboundDeliveryStatus.Sent);

        using var collector = new TelemetryCollector(meter: fixture.Metrics.Meter);
        var updater = CreateUpdater(fixture);

        await updater.UpdateAsync();
        collector.RecordObservableInstruments();

        var pending = collector.MeasurementsNamed(GatewayMetrics.OutboundPending).Single();

        await Assert.That(pending.Value).IsEqualTo(1L);
        await Assert.That(pending.Tags).IsEmpty();
    }

    [Test]
    public async Task Database_failure_does_not_escape_from_updater()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new GatewayMetrics());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IOptions<FlowOptions>>(Options.Create(FlowOptionsForTests()));
        services.AddScoped<GatewayDbContext>(_ => throw new InvalidOperationException("db unavailable"));

        using var provider = services.BuildServiceProvider();
        var updater = ActivatorUtilities.CreateInstance<OutboundBacklogMetricsUpdater>(provider);

        await updater.UpdateAsync();
    }

    private static OutboundBacklogMetricsUpdater CreateUpdater(OutboundFixture fixture)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(fixture.Metrics);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IOptions<FlowOptions>>(Options.Create(FlowOptionsForTests()));
        services.AddScoped(_ => fixture.CreateDb());

        var provider = services.BuildServiceProvider();
        return ActivatorUtilities.CreateInstance<OutboundBacklogMetricsUpdater>(provider);
    }

    private static FlowOptions FlowOptionsForTests() => new()
    {
        Integration = OutboundFixture.IntegrationId,
        InstanceId = "test",
    };

    private static async Task AddAsync(
        OutboundFixture fixture,
        string messageId,
        string address,
        bool withOperation,
        DateTimeOffset createdAt,
        OutboundDeliveryStatus status = OutboundDeliveryStatus.Pending)
    {
        using var db = fixture.CreateDb();

        if (withOperation)
        {
            db.OutboundOperations.Add(new OutboundOperation
            {
                Id = Guid.NewGuid(),
                MessageId = messageId,
                ProductId = OutboundFixture.ProductId,
                ChannelName = OutboundFixture.ChannelName,
                Status = OutboundOperationStatus.Completed,
                Outcome = OutcomeNames.HttpResponse,
                HttpStatusCode = 200,
                AttemptCount = 1,
                CreatedAt = createdAt,
                CompletedAt = createdAt,
            });
        }

        db.OutboundMessageLogs.Add(new OutboundMessageLog
        {
            Id = Guid.NewGuid(),
            MessageId = messageId,
            ProductId = OutboundFixture.ProductId,
            ChannelName = OutboundFixture.ChannelName,
            DeliveryAddress = address,
            HttpMethod = "GET",
            RelativePath = "/api/contracts",
            RequestBody = [],
            ResponseBody = [],
            Outcome = OutcomeNames.HttpResponse,
            AttemptCount = 1,
            DeliveryStatus = status,
            CreatedAt = createdAt,
        });

        await db.SaveChangesAsync();
    }
}
