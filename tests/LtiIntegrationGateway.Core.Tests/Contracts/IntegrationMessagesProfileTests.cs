using Google.Protobuf;
using LtiIntegrationGateway.Contracts.Messages.V1;
using LtiIntegrationGateway.Tests.TestSupport;

namespace LtiIntegrationGateway.Tests.Contracts;

public class IntegrationMessagesProfileTests
{
    [Test]
    public async Task Unknown_field_does_not_break_parsing()
    {
        const string json = """
            {
              "messageId": "m-1",
              "productId": "dpv",
              "channelName": "bank-a",
              "relativePath": "/api/contracts",
              "httpMethod": "GET",
              "fieldFromFutureVersion": "ignored"
            }
            """;

        var parsed = ContractJson.Parse<OutboundRequest>(json);

        await Assert.That(parsed.MessageId).IsEqualTo("m-1");
        await Assert.That(parsed.ProductId).IsEqualTo("dpv");
        await Assert.That(parsed.HttpMethod).IsEqualTo("GET");
    }

    [Test]
    public async Task Wire_names_are_camel_case_and_snake_case_is_accepted()
    {
        var json = ContractJson.Serialize(new OutboundRequest
        {
            MessageId = "m-1",
            ProductId = "dpv",
            ChannelName = "bank-a",
            RelativePath = "/api/contracts",
            HttpMethod = "GET",
        });

        await Assert.That(json.Contains("\"messageId\"", StringComparison.Ordinal)).IsTrue();
        await Assert.That(json.Contains("\"message_id\"", StringComparison.Ordinal)).IsFalse();

        var parsedFromSnakeCase = ContractJson.Parse<OutboundRequest>("""
            {
              "message_id": "m-2",
              "product_id": "dpv",
              "channel_name": "bank-a",
              "relative_path": "/api/contracts",
              "http_method": "GET"
            }
            """);

        await Assert.That(parsedFromSnakeCase.MessageId).IsEqualTo("m-2");
    }

    [Test]
    public async Task Body_is_encoded_as_base64_on_the_wire()
    {
        var body = ByteString.CopyFromUtf8("{\"a\":1}");

        var json = ContractJson.Serialize(new OutboundRequest
        {
            MessageId = "m-1",
            ProductId = "dpv",
            ChannelName = "bank-a",
            RelativePath = "/api/contracts",
            HttpMethod = "POST",
            ContentType = "application/json",
            Body = body,
        });

        await Assert.That(json.Contains(Convert.ToBase64String(body.ToByteArray()), StringComparison.Ordinal))
            .IsTrue();
    }

    [Test]
    public async Task Absent_optional_field_differs_from_empty_value()
    {
        var absent = ContractJson.Parse<OutboundRequest>("""
            { "messageId": "m-1", "productId": "dpv", "channelName": "bank-a",
              "relativePath": "/p", "httpMethod": "GET" }
            """);

        var explicitlyEmpty = ContractJson.Parse<OutboundRequest>("""
            { "messageId": "m-1", "productId": "dpv", "channelName": "bank-a",
              "relativePath": "/p", "httpMethod": "GET", "idempotencyKey": "" }
            """);

        await Assert.That(absent.HasIdempotencyKey).IsFalse();
        await Assert.That(explicitlyEmpty.HasIdempotencyKey).IsTrue();
        await Assert.That(explicitlyEmpty.IdempotencyKey).IsEqualTo(string.Empty);

        var jsonWithoutOptional = ContractJson.Serialize(absent);
        await Assert.That(jsonWithoutOptional.Contains("idempotencyKey", StringComparison.Ordinal)).IsFalse();
    }
}
