using System.Text;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using LtiIntegrationGateway.Contracts.Messages.V1;
using LtiIntegrationGateway.Tests.TestSupport;
namespace LtiIntegrationGateway.Tests.Contracts;

public class IntegrationMessagesRoundTripTests
{
    private static readonly byte[] JsonBody = Encoding.UTF8.GetBytes(
        "{\"contractNumber\":\"DKP-456\",\"amount\":150000.00}");

    [Test]
    public async Task Outbound_request_round_trips_body_and_query_parameters()
    {
        var request = new OutboundRequest
        {
            MessageId = "d7a7e0c5-3f62-48e4-8a18-55d19e219cc0",
            ProductId = "dpv",
            ChannelName = "bank-a",
            RelativePath = "/api/contracts",
            HttpMethod = "POST",
            ContentType = "application/json",
            Body = ByteString.CopyFrom(JsonBody),
            CorrelationId = "process-123",
            IdempotencyKey = "dkp-456",
        };

        request.QueryParameters.Add(new QueryParameter { Name = "account", Value = "1" });
        request.QueryParameters.Add(new QueryParameter { Name = "account", Value = "2" });
        request.QueryParameters.Add(new QueryParameter { Name = "mode", Value = "full" });

        var parsed = ContractJson.Parse<OutboundRequest>(ContractJson.Serialize(request));

        await Assert.That(parsed.Body.ToByteArray().SequenceEqual(JsonBody)).IsTrue();
        await Assert.That(parsed.QueryParameters.Count).IsEqualTo(3);
        await Assert.That(parsed.QueryParameters[0].Name).IsEqualTo("account");
        await Assert.That(parsed.QueryParameters[1].Value).IsEqualTo("2");
        await Assert.That(parsed.QueryParameters[2].Value).IsEqualTo("full");
        await Assert.That(parsed.IdempotencyKey).IsEqualTo("dkp-456");
    }

    [Test]
    public async Task Outbound_result_round_trips_outcome_and_bodies()
    {
        var result = new OutboundResult
        {
            MessageId = "d7a7e0c5-3f62-48e4-8a18-55d19e219cc0",
            ProductId = "dpv",
            ChannelName = "bank-a",
            Outcome = Outcome.HttpResponse,
            HttpStatusCode = 200,
            ContentType = "application/json",
            ResponseBody = ByteString.CopyFromUtf8("{\"status\":\"ok\"}"),
            AttemptCount = 2,
        };

        var parsed = ContractJson.Parse<OutboundResult>(ContractJson.Serialize(result));

        await Assert.That(parsed.Outcome).IsEqualTo(Outcome.HttpResponse);
        await Assert.That(parsed.HttpStatusCode).IsEqualTo(200);
        await Assert.That(parsed.ResponseBody.ToStringUtf8()).IsEqualTo("{\"status\":\"ok\"}");
        await Assert.That(parsed.AttemptCount).IsEqualTo(2);
    }

    [Test]
    public async Task Outbound_result_without_response_has_no_status_and_has_error_code()
    {
        var result = new OutboundResult
        {
            MessageId = "d7a7e0c5-3f62-48e4-8a18-55d19e219cc0",
            ProductId = "dpv",
            ChannelName = "bank-a",
            Outcome = Outcome.TransportError,
            ErrorCode = "TIMEOUT",
            AttemptCount = 4,
        };

        var json = ContractJson.Serialize(result);
        var parsed = ContractJson.Parse<OutboundResult>(json);

        await Assert.That(parsed.HasHttpStatusCode).IsFalse();
        await Assert.That(parsed.ErrorCode).IsEqualTo("TIMEOUT");
        await Assert.That(json.Contains("httpStatusCode", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task Inbound_message_round_trips_route_and_timestamp()
    {
        var receivedAt = Timestamp.FromDateTimeOffset(new DateTimeOffset(2026, 10, 29, 10, 15, 0, TimeSpan.Zero));

        var message = new InboundMessage
        {
            MessageId = "f20f2b25-64b0-4ad2-8d0a-4f4bbfdd53d1",
            ChannelName = "bank-a",
            HttpMethod = "POST",
            RelativePath = "/callbacks/contracts/123/status",
            RouteTemplate = "/callbacks/contracts/{id}/status",
            ContentType = "application/json",
            Body = ByteString.CopyFromUtf8("{\"status\":\"Signed\"}"),
            ReceivedAt = receivedAt,
        };

        message.QueryParameters.Add(new QueryParameter { Name = "source", Value = "bank" });
        message.QueryParameters.Add(new QueryParameter { Name = "source", Value = "mirror" });
        message.ReplyExpected = true;

        var parsed = ContractJson.Parse<InboundMessage>(ContractJson.Serialize(message));

        await Assert.That(parsed.RelativePath).IsEqualTo("/callbacks/contracts/123/status");
        await Assert.That(parsed.RouteTemplate).IsEqualTo("/callbacks/contracts/{id}/status");
        await Assert.That(parsed.ReceivedAt).IsEqualTo(receivedAt);
        await Assert.That(parsed.QueryParameters.Count).IsEqualTo(2);
        await Assert.That(parsed.QueryParameters[1].Value).IsEqualTo("mirror");
        await Assert.That(parsed.ReplyExpected).IsTrue();
    }

    [Test]
    public async Task Inbound_response_round_trips_optional_content_type_and_headers()
    {
        var response = new InboundResponse
        {
            MessageId = "f20f2b25-64b0-4ad2-8d0a-4f4bbfdd53d1",
            HttpStatusCode = 201,
            ContentType = "application/json",
            Body = ByteString.CopyFromUtf8("{\"ok\":true}"),
            ErrorCode = "product_ok",
        };

        response.Headers.Add(new InboundResponseHeader { Name = "Location", Value = "/result/1" });
        response.Headers.Add(new InboundResponseHeader { Name = "Location", Value = "/result/2" });

        var parsed = ContractJson.Parse<InboundResponse>(ContractJson.Serialize(response));

        await Assert.That(parsed.MessageId).IsEqualTo(response.MessageId);
        await Assert.That(parsed.HttpStatusCode).IsEqualTo(201);
        await Assert.That(parsed.ContentType).IsEqualTo("application/json");
        await Assert.That(parsed.Body.ToStringUtf8()).IsEqualTo("{\"ok\":true}");
        await Assert.That(parsed.Headers.Count).IsEqualTo(2);
        await Assert.That(parsed.Headers[1].Value).IsEqualTo("/result/2");
        await Assert.That(parsed.ErrorCode).IsEqualTo("product_ok");
    }
}
