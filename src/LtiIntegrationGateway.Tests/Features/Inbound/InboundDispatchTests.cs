using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Inbound;
using LtiIntegrationGateway.Tests.TestSupport;
using Microsoft.AspNetCore.Http;

namespace LtiIntegrationGateway.Tests.Features.Inbound;

public class InboundDispatchTests
{
    private const string Schemas = """
    {
      "application/json": {
        "type": "object",
        "required": ["contractNumber"],
        "properties": {
          "contractNumber": { "type": "string" },
          "amount": { "type": "number", "minimum": 0 }
        }
      }
    }
    """;

    private const string ValidBody = """{ "contractNumber": "DKP-456", "amount": 10 }""";

    [Test]
    public async Task Accepted_call_is_published_with_request_details()
    {
        await using var fixture = new InboundFixture();
        await fixture.WarmUpAsync("POST", "/callbacks/contracts/{id}/status", Schemas);

        var status = await fixture.InvokeAsync(
            fixture.Route(),
            method: "POST",
            path: "/callbacks/contracts/42/status",
            template: "/callbacks/contracts/{id}/status",
            body: ValidBody,
            queryString: "?source=bank&tag=a&tag=b");

        var messages = await fixture.Received.WaitForCountAsync(1, TimeSpan.FromSeconds(10));
        var message = messages[0];

        await Assert.That(status).IsEqualTo(StatusCodes.Status202Accepted);
        await Assert.That(message.ChannelName).IsEqualTo(InboundFixture.ChannelName);
        await Assert.That(message.HttpMethod).IsEqualTo("POST");
        await Assert.That(message.RelativePath).IsEqualTo("/callbacks/contracts/42/status");
        await Assert.That(message.RouteTemplate).IsEqualTo("/callbacks/contracts/{id}/status");
        await Assert.That(message.OperationId).IsEqualTo("receiveContract");
        await Assert.That(message.ContentType).IsEqualTo("application/json");
        await Assert.That(message.Body.ToStringUtf8()).IsEqualTo(ValidBody);
        await Assert.That(message.QueryParameters.Select(parameter => parameter.Name).ToArray())
            .IsEquivalentTo(new[] { "source", "tag", "tag" });
        await Assert.That(message.QueryParameters.Select(parameter => parameter.Value).ToArray())
            .IsEquivalentTo(new[] { "bank", "a", "b" });
        await Assert.That(message.ReceivedAt).IsNotNull();
        await Assert.That(message.MessageId.Length).IsEqualTo(32);
    }

    [Test]
    public async Task Repeated_identical_calls_produce_distinct_messages()
    {
        await using var fixture = new InboundFixture();
        await fixture.WarmUpAsync("POST", "/callbacks/contracts", Schemas);

        await fixture.InvokeAsync(fixture.Route(), body: ValidBody);
        await fixture.InvokeAsync(fixture.Route(), body: ValidBody);

        var messages = await fixture.Received.WaitForCountAsync(2, TimeSpan.FromSeconds(10));

        await Assert.That(messages[0].MessageId).IsNotEqualTo(messages[1].MessageId);
        await Assert.That((await fixture.LogsAsync()).Count).IsEqualTo(2);
    }

    [Test]
    public async Task Too_deep_json_is_rejected_as_bad_request()
    {
        await using var fixture = new InboundFixture();
        await fixture.WarmUpAsync("POST", "/callbacks/contracts", Schemas);

        var deep = new string('[', 70) + "1" + new string(']', 70);

        var status = await fixture.InvokeAsync(fixture.Route(), body: deep);

        await Assert.That(status).IsEqualTo(StatusCodes.Status400BadRequest);
        await Assert.That(fixture.Received.Messages.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Too_many_query_parameters_are_rejected_as_bad_request()
    {
        await using var fixture = new InboundFixture();
        await fixture.WarmUpAsync("POST", "/callbacks/contracts", Schemas);

        var query = "?" + string.Join("&", Enumerable.Range(0, 65).Select(index => $"p{index}=v"));

        var status = await fixture.InvokeAsync(fixture.Route(), body: ValidBody, queryString: query);

        await Assert.That(status).IsEqualTo(StatusCodes.Status400BadRequest);
        await Assert.That(fixture.Received.Messages.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Malformed_json_is_rejected_as_bad_request()
    {
        await using var fixture = new InboundFixture();
        await fixture.WarmUpAsync("POST", "/callbacks/contracts", Schemas);

        var status = await fixture.InvokeAsync(fixture.Route(), body: "{ not-a-json");

        await Assert.That(status).IsEqualTo(StatusCodes.Status400BadRequest);
        await Assert.That(fixture.Received.Messages.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Unsupported_content_type_is_rejected()
    {
        await using var fixture = new InboundFixture();
        await fixture.WarmUpAsync("POST", "/callbacks/contracts", Schemas);

        var status = await fixture.InvokeAsync(fixture.Route(), body: "contractNumber=DKP-1", contentType: "text/plain");

        await Assert.That(status).IsEqualTo(StatusCodes.Status415UnsupportedMediaType);
        await Assert.That(fixture.Received.Messages.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Body_violating_schema_is_not_published()
    {
        await using var fixture = new InboundFixture();
        await fixture.WarmUpAsync("POST", "/callbacks/contracts", Schemas);

        var status = await fixture.InvokeAsync(fixture.Route(), body: """{ "amount": 10 }""");

        var logs = await fixture.LogsAsync();

        await Assert.That(status).IsEqualTo(StatusCodes.Status422UnprocessableEntity);
        await Assert.That(fixture.Received.Messages.Count).IsEqualTo(0);
        await Assert.That(logs.Count).IsEqualTo(1);
        await Assert.That(logs[0].DeliveryStatus).IsEqualTo(InboundDeliveryStatus.Failed);
        await Assert.That(logs[0].ErrorCode).IsEqualTo("schema: тело не соответствует схеме");
    }

    [Test]
    public async Task Body_without_declared_schema_is_rejected()
    {
        await using var fixture = new InboundFixture();

        var status = await fixture.InvokeAsync(fixture.Route(), body: ValidBody);

        await Assert.That(status).IsEqualTo(StatusCodes.Status400BadRequest);
        await Assert.That(fixture.Received.Messages.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Delivery_failure_returns_service_unavailable_and_marks_log()
    {
        await using var fixture = new InboundFixture();
        await fixture.WarmUpAsync("POST", "/callbacks/contracts", Schemas);

        await fixture.BreakGatewayBusAsync();

        var status = await fixture.InvokeAsync(fixture.Route(), body: ValidBody);

        var logs = await fixture.LogsAsync();

        await Assert.That(status).IsEqualTo(StatusCodes.Status503ServiceUnavailable);
        await Assert.That(logs.Count).IsEqualTo(1);
        await Assert.That(logs[0].DeliveryStatus).IsEqualTo(InboundDeliveryStatus.Failed);
        await Assert.That(logs[0].ErrorCode).IsEqualTo("delivery");
    }

    [Test]
    public async Task Accepted_call_is_logged_with_sent_status_body_and_revision()
    {
        await using var fixture = new InboundFixture();
        await fixture.WarmUpAsync("POST", "/callbacks/contracts", Schemas);

        await fixture.InvokeAsync(fixture.Route(), body: ValidBody, queryString: "?source=bank");

        var logs = await fixture.LogsAsync();

        await Assert.That(logs.Count).IsEqualTo(1);
        await Assert.That(logs[0].DeliveryStatus).IsEqualTo(InboundDeliveryStatus.Sent);
        await Assert.That(logs[0].ErrorCode).IsNull();
        await Assert.That(logs[0].ContractVersionId).IsEqualTo(InboundFixture.ContractVersionId);
        await Assert.That(logs[0].ChannelName).IsEqualTo(InboundFixture.ChannelName);
        await Assert.That(logs[0].HttpMethod).IsEqualTo("POST");
        await Assert.That(logs[0].RelativePath).IsEqualTo("/callbacks/contracts");
        await Assert.That(logs[0].RouteTemplate).IsEqualTo("/callbacks/contracts");
        await Assert.That(logs[0].OperationId).IsEqualTo("receiveContract");
        await Assert.That(System.Text.Encoding.UTF8.GetString(logs[0].RequestBody)).IsEqualTo(ValidBody);
        await Assert.That(logs[0].QueryParameters).Contains("source");
    }
}
