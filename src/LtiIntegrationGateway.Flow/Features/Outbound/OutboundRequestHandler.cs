using LtiIntegrationGateway.Contracts;
using LtiIntegrationGateway.Contracts.Messages.V1;
using Rebus.Exceptions;
using Rebus.Handlers;
using Rebus.Messages;
using Rebus.Pipeline;

namespace LtiIntegrationGateway.Features.Outbound;

/// <summary>
/// Адаптер Rebus: читает адреса из заголовков входящего сообщения и передаёт их в сценарий обработки.
/// </summary>
public sealed class OutboundRequestHandler(ProcessOutboundRequestHandler processor) : IHandleMessages<OutboundRequest>
{
    public async Task Handle(OutboundRequest message)
    {
        var problems = MessageContractValidator.ValidateOutboundRequest(message);

        if (problems.Count > 0)
        {
            // Невалидный DTO не журналируется: у него может не быть message_id и канала.
            throw new FailFastException(
                $"OutboundRequest: нарушены обязательные поля: {string.Join(", ", problems)}");
        }

        var headers = MessageContext.Current?.Headers;

        await processor.HandleAsync(new ProcessOutboundRequestCommand(
            message,
            ReadHeader(headers, Headers.SenderAddress),
            ReadHeader(headers, Headers.ReturnAddress)));
    }

    private static string? ReadHeader(IReadOnlyDictionary<string, string>? headers, string name) =>
        headers is not null && headers.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
}
