using LtiIntegrationGateway.Contracts.Messages.V1;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities;
using Rebus.Bus;

namespace LtiIntegrationGateway.Features.Outbound;

/// <summary>Отправка результата продукту в проверенную очередь отправителя.</summary>
public sealed class PublishOutboundResultHandler(IBus bus, OutboundOperationRecorder recorder)
{
    /// <summary>
    /// Только отправка, без фиксации признака доставки: диспетчер и отказ обрабатывают сбой отправки
    /// и сбой записи статуса по отдельности.
    /// </summary>
    public Task SendAsync(OutboundResult result, string returnAddress) =>
        bus.Advanced.Routing.Send(returnAddress, result);

    /// <summary>
    /// Отправка и фиксация признака доставки у записи журнала, созданной этой же операцией: чужие
    /// записи с тем же <c>message_id</c> не помечаются.
    /// </summary>
    public async Task HandleAsync(
        OutboundResult result,
        string returnAddress,
        OutboundMessageLog log,
        CancellationToken cancellationToken = default)
    {
        await SendAsync(result, returnAddress);

        await recorder.MarkDeliveredAsync(log, cancellationToken);
    }

    /// <summary>
    /// Отправка результата с отметкой записи журнала по <c>message_id</c>: применяется при повторе,
    /// где <c>message_id</c> принадлежит самой восстанавливаемой операции.
    /// </summary>
    public async Task HandleAsync(
        OutboundResult result,
        string returnAddress,
        CancellationToken cancellationToken = default)
    {
        await SendAsync(result, returnAddress);

        await recorder.MarkDeliveredAsync(result.MessageId, cancellationToken);
    }
}
