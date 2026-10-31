using LtiIntegrationGateway.Features.Outbound;
using LtiIntegrationGateway.Infrastructure.Observability;
using LtiIntegrationGateway.Tests.TestSupport;

namespace LtiIntegrationGateway.Tests.Features.Observability;

/// <summary>Проверка имён, значений и безопасных измерений бизнес-метрик.</summary>
[NotInParallel]
public class GatewayMetricsTests
{
    [Test]
    public async Task Inbound_and_outbound_measurements_have_limited_tags()
    {
        using var metrics = new GatewayMetrics();
        using var collector = new TelemetryCollector(meter: metrics.Meter);

        metrics.RecordInbound("bank-a", "POST", "/callbacks/contracts", 202, null, 12.5);
        metrics.RecordOutbound("dpv", "bank-a", OutcomeNames.HttpResponse, null, 20.5, 1);
        metrics.RecordDelivery("bank-a", succeeded: true);
        metrics.SetPending(3);

        collector.RecordObservableInstruments();

        var inboundRequests = collector.MeasurementsNamed(GatewayMetrics.InboundRequests);
        var outboundOperations = collector.MeasurementsNamed(GatewayMetrics.OutboundOperations);
        var deliveries = collector.MeasurementsNamed(GatewayMetrics.OutboundDeliveries);
        var pending = collector.MeasurementsNamed(GatewayMetrics.OutboundPending);

        await Assert.That(inboundRequests.Count).IsEqualTo(1);
        await Assert.That(inboundRequests[0].Value).IsEqualTo(1L);
        await Assert.That(inboundRequests[0].Tags["channel"]).IsEqualTo("bank-a");
        await Assert.That(inboundRequests[0].Tags["http.response.status_code"]).IsEqualTo(202);
        await Assert.That(inboundRequests[0].Tags.ContainsKey("message_id")).IsFalse();

        await Assert.That(outboundOperations.Count).IsEqualTo(1);
        await Assert.That(outboundOperations[0].Tags["product_id"]).IsEqualTo("dpv");
        await Assert.That(outboundOperations[0].Tags["outcome"]).IsEqualTo(OutcomeNames.HttpResponse);
        await Assert.That(outboundOperations[0].Tags.ContainsKey("message_id")).IsFalse();

        await Assert.That(deliveries.Count).IsEqualTo(1);
        await Assert.That(deliveries[0].Tags["outcome"]).IsEqualTo("delivered");

        await Assert.That(pending.Count).IsEqualTo(1);
        await Assert.That(pending[0].Value).IsEqualTo(3L);
        await Assert.That(pending[0].Tags).IsEmpty();
    }

    [Test]
    public async Task Empty_error_code_is_not_added_as_measurement_tag()
    {
        using var metrics = new GatewayMetrics();
        using var collector = new TelemetryCollector(meter: metrics.Meter);

        metrics.RecordInbound("bank-a", "GET", "/health", 200, "", 1);

        var measurement = collector.MeasurementsNamed(GatewayMetrics.InboundRequests).Single();

        await Assert.That(measurement.Tags.ContainsKey("error_code")).IsFalse();
    }
}
