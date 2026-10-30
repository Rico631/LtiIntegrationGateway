using LtiIntegrationGateway.Contracts.Messages.V1;
using Rebus.Bus;

namespace LtiIntegrationGateway.Features.Outbound;

/// <summary>Отправка результата продукту в проверенную очередь отправителя.</summary>
public sealed class PublishOutboundResultHandler(IBus bus, OutboundOperationRecorder recorder)
{
    public async Task HandleAsync(
        OutboundResult result,
        string returnAddress,
        CancellationToken cancellationToken = default)
    {
        await bus.Advanced.Routing.Send(returnAddress, result);

        await recorder.MarkDeliveredAsync(result.MessageId, cancellationToken);
    }
}
