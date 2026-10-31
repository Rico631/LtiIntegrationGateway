using LtiIntegrationGateway.Infrastructure.Observability;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;
using LtiIntegrationGateway.Tests.TestSupport;
using Microsoft.AspNetCore.Http;

namespace LtiIntegrationGateway.Tests.Features.Inbound;

/// <summary>Проверка телеметрии входящего контура без коллектора.</summary>
[NotInParallel]
public class InboundTelemetryTests
{
    [Test]
    public async Task Successful_request_creates_activity_and_metrics_without_sensitive_data()
    {
        await using var fixture = new InboundFixture();
        using var collector = new TelemetryCollector(meter: fixture.Metrics.Meter);

        var status = await fixture.InvokeAsync(
            fixture.Route(),
            headers: new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer MARKER-AUTH",
                ["Cookie"] = "MARKER-COOKIE",
            });

        await Assert.That(status).IsEqualTo(StatusCodes.Status202Accepted);

        var activity = collector.ActivitiesNamed(GatewayTelemetry.InboundRequestActivity).Single();
        var requests = collector.MeasurementsNamed(GatewayMetrics.InboundRequests).Single();

        await Assert.That(activity.GetTagItem(GatewayTelemetry.ChannelTag)).IsEqualTo("bank-a");
        await Assert.That(activity.GetTagItem(GatewayTelemetry.HttpMethodTag)).IsEqualTo("POST");
        await Assert.That(activity.GetTagItem(GatewayTelemetry.HttpRouteTag)).IsEqualTo("/callbacks/contracts");
        await Assert.That(activity.GetTagItem(GatewayTelemetry.OperationIdTag)).IsEqualTo("receiveContract");
        await Assert.That(activity.GetTagItem(GatewayTelemetry.HttpStatusCodeTag)).IsEqualTo(202);

        await Assert.That(requests.Value).IsEqualTo(1L);
        await Assert.That(requests.Tags["channel"]).IsEqualTo("bank-a");
        await Assert.That(requests.Tags["http.response.status_code"]).IsEqualTo(202);

        var values = activity.TagObjects.Select(tag => tag.Value?.ToString() ?? string.Empty).ToArray();
        var metricValues = requests.Tags.Values.Select(value => value?.ToString() ?? string.Empty).ToArray();

        await Assert.That(values.Any(value => value.Contains("MARKER-AUTH", StringComparison.Ordinal))).IsFalse();
        await Assert.That(values.Any(value => value.Contains("MARKER-COOKIE", StringComparison.Ordinal))).IsFalse();
        await Assert.That(metricValues.Any(value => value.Contains("MARKER-AUTH", StringComparison.Ordinal))).IsFalse();
        await Assert.That(metricValues.Any(value => value.Contains("MARKER-COOKIE", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task Rejected_request_records_safe_error_code_and_status()
    {
        await using var fixture = new InboundFixture();
        using var collector = new TelemetryCollector(meter: fixture.Metrics.Meter);

        var status = await fixture.InvokeAsync(
            fixture.Route(ChannelAuthType.ApiKey, "bank-a.inbound", headerName: "X-Bank-Key"));

        await Assert.That(status).IsEqualTo(StatusCodes.Status503ServiceUnavailable);

        var activity = collector.ActivitiesNamed(GatewayTelemetry.InboundRequestActivity).Single();
        var requests = collector.MeasurementsNamed(GatewayMetrics.InboundRequests).Single();

        await Assert.That(activity.GetTagItem(GatewayTelemetry.ErrorCodeTag)).IsEqualTo("auth");
        await Assert.That(activity.GetTagItem(GatewayTelemetry.HttpStatusCodeTag)).IsEqualTo(503);
        await Assert.That(requests.Tags["error_code"]).IsEqualTo("auth");
        await Assert.That(requests.Tags["http.response.status_code"]).IsEqualTo(503);
    }

    [Test]
    public async Task Request_body_and_query_values_are_not_put_into_tags()
    {
        await using var fixture = new InboundFixture();
        using var collector = new TelemetryCollector(meter: fixture.Metrics.Meter);

        var status = await fixture.InvokeAsync(
            fixture.Route(),
            body: """{"secret":"MARKER-SECRET"}""",
            queryString: "?marker=MARKER-QUERY");

        await Assert.That(status).IsEqualTo(StatusCodes.Status400BadRequest);

        var activity = collector.ActivitiesNamed(GatewayTelemetry.InboundRequestActivity).Single();
        var requests = collector.MeasurementsNamed(GatewayMetrics.InboundRequests).Single();
        var values = activity.TagObjects.Select(tag => tag.Value?.ToString() ?? string.Empty).ToArray();
        var metricValues = requests.Tags.Values.Select(value => value?.ToString() ?? string.Empty).ToArray();

        await Assert.That(values.Any(value => value.Contains("MARKER-SECRET", StringComparison.Ordinal))).IsFalse();
        await Assert.That(values.Any(value => value.Contains("MARKER-QUERY", StringComparison.Ordinal))).IsFalse();
        await Assert.That(metricValues.Any(value => value.Contains("MARKER-SECRET", StringComparison.Ordinal))).IsFalse();
        await Assert.That(metricValues.Any(value => value.Contains("MARKER-QUERY", StringComparison.Ordinal))).IsFalse();
        await Assert.That(activity.GetTagItem(GatewayTelemetry.ErrorCodeTag)).IsEqualTo("body");
    }
}
