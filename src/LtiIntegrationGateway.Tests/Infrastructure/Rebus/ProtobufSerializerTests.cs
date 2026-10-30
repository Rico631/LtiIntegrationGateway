using System.Text;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using LtiIntegrationGateway.Contracts.Messages.V1;
using LtiIntegrationGateway.Infrastructure.Rebus;
using Rebus.Messages;

namespace LtiIntegrationGateway.Tests.Infrastructure.Rebus;

public class ProtobufSerializerTests
{
    private static readonly ProtobufSerializer Serializer = new(new ProtobufMessageRegistry());

    private static Task<TransportMessage> SerializeAsync(IMessage message) =>
        Serializer.Serialize(new Message(new Dictionary<string, string>(StringComparer.Ordinal), message));

    private static async Task<T> RoundTripAsync<T>(T message)
        where T : IMessage<T>
    {
        var transportMessage = await SerializeAsync(message);
        var deserialized = await Serializer.Deserialize(transportMessage);

        return (T)deserialized.Body;
    }

    [Test]
    public async Task Outbound_request_is_serialized_with_type_header_and_base64_body()
    {
        var body = ByteString.CopyFromUtf8("{\"contractNumber\":\"DKP-456\"}");

        var transportMessage = await SerializeAsync(new OutboundRequest
        {
            MessageId = "m-1",
            ProductId = "dpv",
            ChannelName = "bank-a",
            RelativePath = "/api/contracts",
            HttpMethod = "POST",
            ContentType = "application/json",
            Body = body,
        });

        await Assert
            .That(transportMessage.Headers[Headers.Type])
            .IsEqualTo("lti.gateway.messages.v1.OutboundRequest");
        await Assert.That(transportMessage.Headers[Headers.ContentType]).IsEqualTo("application/json");

        var json = Encoding.UTF8.GetString(transportMessage.Body);
        await Assert
            .That(json.Contains(Convert.ToBase64String(body.ToByteArray()), StringComparison.Ordinal))
            .IsTrue();
    }

    [Test]
    public async Task Outbound_result_round_trips()
    {
        var parsed = await RoundTripAsync(new OutboundResult
        {
            MessageId = "m-2",
            ProductId = "dpv",
            ChannelName = "bank-a",
            Outcome = Outcome.HttpResponse,
            HttpStatusCode = 200,
            ResponseBody = ByteString.CopyFromUtf8("{\"status\":\"ok\"}"),
            AttemptCount = 2,
        });

        await Assert.That(parsed.Outcome).IsEqualTo(Outcome.HttpResponse);
        await Assert.That(parsed.HttpStatusCode).IsEqualTo(200);
        await Assert.That(parsed.ResponseBody.ToStringUtf8()).IsEqualTo("{\"status\":\"ok\"}");
        await Assert.That(parsed.AttemptCount).IsEqualTo(2);
    }

    [Test]
    public async Task Inbound_message_round_trips()
    {
        var receivedAt = Timestamp.FromDateTimeOffset(new DateTimeOffset(2026, 10, 29, 10, 15, 0, TimeSpan.Zero));

        var message = new InboundMessage
        {
            MessageId = "m-3",
            ChannelName = "bank-a",
            HttpMethod = "POST",
            RelativePath = "/callbacks/contracts/123/status",
            RouteTemplate = "/callbacks/contracts/{id}/status",
            ReceivedAt = receivedAt,
        };

        message.QueryParameters.Add(new QueryParameter { Name = "source", Value = "bank" });
        message.QueryParameters.Add(new QueryParameter { Name = "source", Value = "mirror" });

        var parsed = await RoundTripAsync(message);

        await Assert.That(parsed.RelativePath).IsEqualTo("/callbacks/contracts/123/status");
        await Assert.That(parsed.RouteTemplate).IsEqualTo("/callbacks/contracts/{id}/status");
        await Assert.That(parsed.ReceivedAt).IsEqualTo(receivedAt);
        await Assert.That(parsed.QueryParameters.Count).IsEqualTo(2);
        await Assert.That(parsed.QueryParameters[1].Value).IsEqualTo("mirror");
    }

    [Test]
    public async Task Unknown_type_fails_with_type_name()
    {
        var transportMessage = new TransportMessage(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Headers.Type] = "lti.gateway.messages.v1.UnknownMessage",
                [Headers.ContentType] = "application/json",
            },
            Encoding.UTF8.GetBytes("{}"));

        var captured = await CaptureAsync(() => Serializer.Deserialize(transportMessage));

        await Assert.That(captured is not null).IsTrue();
        await Assert
            .That(captured!.ToString().Contains("lti.gateway.messages.v1.UnknownMessage", StringComparison.Ordinal))
            .IsTrue();
    }

    [Test]
    public async Task Missing_type_header_fails_with_header_name()
    {
        var transportMessage = new TransportMessage(
            new Dictionary<string, string>(StringComparer.Ordinal),
            Encoding.UTF8.GetBytes("{}"));

        var captured = await CaptureAsync(() => Serializer.Deserialize(transportMessage));

        await Assert.That(captured is not null).IsTrue();
        await Assert.That(captured!.ToString().Contains(Headers.Type, StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task Non_protobuf_body_fails_with_clr_type_name()
    {
        var captured = await CaptureAsync(() => Serializer.Serialize(new Message(
            new Dictionary<string, string>(StringComparer.Ordinal),
            new NotAProtobufMessage())));

        await Assert.That(captured is not null).IsTrue();
        await Assert
            .That(captured!.ToString().Contains(nameof(NotAProtobufMessage), StringComparison.Ordinal))
            .IsTrue();
    }

    private static async Task<Exception?> CaptureAsync(Func<Task> action)
    {
        try
        {
            await action();

            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private sealed record NotAProtobufMessage;
}