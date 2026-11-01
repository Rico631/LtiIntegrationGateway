using System.Text.Json;
using Google.Protobuf;
using LtiIntegrationGateway.Contracts.Messages.V1;
using Rebus.Bus;
using Rebus.Handlers;
using Rebus.Messages;
using Rebus.Pipeline;

namespace LtiIntegrationGateway.Simulation.Product.Handlers;

/// <summary>
/// Принимает входящий вызов, переданный шлюзом, пишет его в лог и отвечает на синхронный запрос.
/// </summary>
public sealed class InboundOrderHandler(ILogger<InboundOrderHandler> logger, IBus bus) : IHandleMessages<InboundMessage>
{
    public async Task Handle(InboundMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        logger.LogInformation(
            "Входящий вызов от шлюза: {Method} {Path} (шаблон {Template}), replyExpected={ReplyExpected}, body={Body}",
            message.HttpMethod,
            message.RelativePath,
            message.RouteTemplate,
            message.ReplyExpected,
            message.Body.ToStringUtf8());

        if (!message.ReplyExpected)
        {
            return;
        }

        var headers = MessageContext.Current?.Headers;

        if (headers is null ||
            !headers.TryGetValue(Headers.ReturnAddress, out var returnAddress) ||
            string.IsNullOrWhiteSpace(returnAddress))
        {
            return;
        }

        var replyHeaders = new Dictionary<string, string>(StringComparer.Ordinal);

        if (headers.TryGetValue(Headers.CorrelationId, out var correlationId) &&
            !string.IsNullOrWhiteSpace(correlationId))
        {
            replyHeaders[Headers.CorrelationId] = correlationId;
        }

        var order = JsonSerializer.Serialize(new { orderId = ResolveOrderId(message), status = "created" });

        var response = new InboundResponse
        {
            MessageId = message.MessageId,
            HttpStatusCode = 200,
            ContentType = "application/json",
            Body = ByteString.CopyFromUtf8(order),
        };

        await bus.Advanced.Routing.Send(returnAddress, response, replyHeaders);

        logger.LogInformation("Ответ шлюзу на {Path}: {Order}", message.RelativePath, order);
    }

    /// <summary>Идентификатор заказа из тела запроса; для GET — из пути <c>/order/{orderid}</c>.</summary>
    private static string ResolveOrderId(InboundMessage message)
    {
        if (message.Body.Length > 0)
        {
            try
            {
                using var document = JsonDocument.Parse(message.Body.ToStringUtf8());

                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    var property = document.RootElement
                        .EnumerateObject()
                        .FirstOrDefault(candidate => string.Equals(candidate.Name, "orderid", StringComparison.OrdinalIgnoreCase));

                    if (property.Value.ValueKind == JsonValueKind.String &&
                        property.Value.GetString() is { Length: > 0 } fromBody)
                    {
                        return fromBody;
                    }
                }
            }
            catch (JsonException)
            {
                // Тело не JSON — остаётся путь запроса.
            }
        }

        var segments = message.RelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        return segments.Length > 0 ? segments[^1] : Guid.NewGuid().ToString("N");
    }
}
