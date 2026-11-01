using LtiIntegrationGateway.Features.Outbound;
using LtiIntegrationGateway.Infrastructure.Observability;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Outbound;
using LtiIntegrationGateway.Tests.TestSupport;

namespace LtiIntegrationGateway.Tests.Features.Outbound;

/// <summary>Проверка span и счётчика доставки сохранённого результата.</summary>
[NotInParallel]
public class OutboundDeliveryTelemetryTests
{
    [Test]
    public async Task Successful_delivery_marks_span_and_counter()
    {
        await using var fixture = new OutboundFixture();
        await AddPendingAsync(fixture);

        using var collector = new TelemetryCollector(meter: fixture.Metrics.Meter);
        var bus = new RecordingBus();

        await fixture.CreateDispatcher(bus).DispatchPendingAsync();

        var activity = collector.ActivitiesNamed(GatewayTelemetry.OutboundDeliveryActivity).Single();
        var deliveries = collector.MeasurementsNamed(GatewayMetrics.OutboundDeliveries).Single();

        await Assert.That(activity.GetTagItem(GatewayTelemetry.MessageIdTag)).IsEqualTo("msg-1");
        await Assert.That(activity.GetTagItem(GatewayTelemetry.ChannelTag)).IsEqualTo(OutboundFixture.ChannelName);
        await Assert.That(activity.GetTagItem(GatewayTelemetry.DeliverySucceededTag) is true).IsTrue();
        await Assert.That(activity.GetTagItem(GatewayTelemetry.OutcomeTag)).IsEqualTo("delivered");
        await Assert.That(deliveries.Tags["outcome"]).IsEqualTo("delivered");
    }

    [Test]
    public async Task Failed_delivery_marks_span_and_counter_as_failed()
    {
        await using var fixture = new OutboundFixture();
        await AddPendingAsync(fixture);

        using var collector = new TelemetryCollector(meter: fixture.Metrics.Meter);
        var bus = new RecordingBus((_, _) => true);

        await fixture.CreateDispatcher(bus).DispatchPendingAsync();

        var activity = collector.ActivitiesNamed(GatewayTelemetry.OutboundDeliveryActivity).Single();
        var deliveries = collector.MeasurementsNamed(GatewayMetrics.OutboundDeliveries).Single();

        await Assert.That(activity.GetTagItem(GatewayTelemetry.DeliverySucceededTag) is false).IsTrue();
        await Assert.That(activity.GetTagItem(GatewayTelemetry.ErrorCodeTag)).IsEqualTo("delivery");
        await Assert.That(deliveries.Tags["outcome"]).IsEqualTo("failed");
        await Assert.That(bus.Sent.Count).IsEqualTo(0);
    }

    private static async Task AddPendingAsync(OutboundFixture fixture)
    {
        var createdAt = DateTimeOffset.UtcNow;

        using var db = fixture.CreateDb();

        db.OutboundOperations.Add(new OutboundOperation
        {
            Id = Guid.NewGuid(),
            MessageId = "msg-1",
            ProductId = OutboundFixture.ProductId,
            ChannelName = OutboundFixture.ChannelName,
            Status = OutboundOperationStatus.Completed,
            Outcome = OutcomeNames.HttpResponse,
            HttpStatusCode = 200,
            AttemptCount = 1,
            CreatedAt = createdAt,
            CompletedAt = createdAt,
        });

        db.OutboundMessageLogs.Add(new OutboundMessageLog
        {
            Id = Guid.NewGuid(),
            MessageId = "msg-1",
            ProductId = OutboundFixture.ProductId,
            ChannelName = OutboundFixture.ChannelName,
            DeliveryAddress = OutboundFixture.ProductInbox,
            HttpMethod = "GET",
            RelativePath = "/api/contracts",
            RequestBody = [],
            ResponseBody = "{\"ok\":true}"u8.ToArray(),
            ContentType = "application/json",
            HttpStatusCode = 200,
            Outcome = OutcomeNames.HttpResponse,
            AttemptCount = 1,
            DeliveryStatus = OutboundDeliveryStatus.Pending,
            CreatedAt = createdAt,
        });

        await db.SaveChangesAsync();
    }
}
