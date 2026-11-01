using System.Net;
using LtiIntegrationGateway.Contracts.Messages.V1;
using LtiIntegrationGateway.Features.Outbound;
using LtiIntegrationGateway.Infrastructure.Observability;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;
using LtiIntegrationGateway.Tests.TestSupport;

namespace LtiIntegrationGateway.Tests.Features.Outbound;

/// <summary>Проверка spans и метрик исходящего контура.</summary>
[NotInParallel]
public class OutboundTelemetryTests
{
    [Test]
    public async Task Successful_operation_creates_span_and_metrics()
    {
        await using var fixture = new OutboundFixture();
        await fixture.CreateActiveChannelAsync();
        await fixture.RegisterProductAsync();

        fixture.Http.EnqueueResponse(HttpStatusCode.OK, "{\"ok\":true}");

        using var collector = new TelemetryCollector(meter: fixture.Metrics.Meter);
        using var db = fixture.CreateDb();

        var result = await fixture.CreateProcessor(db).HandleAsync(Command());

        await Assert.That(result.Completed).IsTrue();

        var activity = collector.ActivitiesNamed(GatewayTelemetry.OutboundOperationActivity).Single();
        var operations = collector.MeasurementsNamed(GatewayMetrics.OutboundOperations).Single();
        var attempts = collector.MeasurementsNamed(GatewayMetrics.OutboundAttempts).Single();

        await Assert.That(activity.GetTagItem(GatewayTelemetry.MessageIdTag)).IsEqualTo("msg-1");
        await Assert.That(activity.GetTagItem(GatewayTelemetry.ProductIdTag)).IsEqualTo(OutboundFixture.ProductId);
        await Assert.That(activity.GetTagItem(GatewayTelemetry.ChannelTag)).IsEqualTo(OutboundFixture.ChannelName);
        await Assert.That(activity.GetTagItem(GatewayTelemetry.OutcomeTag)).IsEqualTo(OutcomeNames.HttpResponse);
        await Assert.That(activity.GetTagItem(GatewayTelemetry.AttemptCountTag)).IsEqualTo(1);

        await Assert.That(operations.Tags["outcome"]).IsEqualTo(OutcomeNames.HttpResponse);
        await Assert.That(attempts.Value).IsEqualTo(1);
    }

    [Test]
    public async Task Transport_error_and_refusal_are_recorded_without_message_text()
    {
        await using var fixture = new OutboundFixture();
        await fixture.CreateActiveChannelAsync(
            retryPolicy: new RetryPolicy([RetryRule.TransportError([1])]));
        await fixture.RegisterProductAsync();

        fixture.Http.EnqueueException(new HttpRequestException("MARKER-UPSTREAM"));
        fixture.Http.EnqueueException(new HttpRequestException("MARKER-UPSTREAM"));

        using var collector = new TelemetryCollector(meter: fixture.Metrics.Meter);
        using var db = fixture.CreateDb();

        await fixture.CreateProcessor(db).HandleAsync(Command());

        var transport = collector.ActivitiesNamed(GatewayTelemetry.OutboundOperationActivity).Single();
        var operations = collector.MeasurementsNamed(GatewayMetrics.OutboundOperations).Single();
        var values = transport.TagObjects.Select(tag => tag.Value?.ToString() ?? string.Empty).ToArray();

        await Assert.That(transport.GetTagItem(GatewayTelemetry.OutcomeTag))
            .IsEqualTo(OutcomeNames.TransportError);
        await Assert.That(values.Any(value => value.Contains("MARKER-UPSTREAM", StringComparison.Ordinal))).IsFalse();
        await Assert.That(operations.Tags["outcome"]).IsEqualTo(OutcomeNames.TransportError);
    }

    [Test]
    public async Task Refusal_is_recorded_without_external_call()
    {
        await using var fixture = new OutboundFixture();
        await fixture.CreateActiveChannelAsync();
        await fixture.RegisterProductAsync();

        using var collector = new TelemetryCollector(meter: fixture.Metrics.Meter);
        using var db = fixture.CreateDb();

        var request = OutboundFixture.CreateRequest();
        request.ProductId = "unknown-product";

        await fixture.CreateProcessor(db).HandleAsync(Command(request));

        var activity = collector.ActivitiesNamed(GatewayTelemetry.OutboundOperationActivity).Single();
        var operations = collector.MeasurementsNamed(GatewayMetrics.OutboundOperations).Single();

        await Assert.That(activity.GetTagItem(GatewayTelemetry.OutcomeTag))
            .IsEqualTo(OutcomeNames.GatewayRefused);
        await Assert.That(activity.GetTagItem(GatewayTelemetry.ErrorCodeTag))
            .IsEqualTo(OutboundErrorCodes.ProductNotAllowed);
        await Assert.That(operations.Tags["outcome"]).IsEqualTo(OutcomeNames.GatewayRefused);
    }

    private static ProcessOutboundRequestCommand Command(OutboundRequest? request = null) =>
        new(request ?? OutboundFixture.CreateRequest(), OutboundFixture.ProductInbox, OutboundFixture.ProductInbox);
}
