using LtiIntegrationGateway.Contracts.Messages.V1;
using Rebus.Bus;

namespace LtiIntegrationGateway.Features.Inbound;

/// <summary>
/// Публикация принятого сообщения в очередь получателя. Отдельный интерфейс отделяет приём от
/// транспорта: сбой передачи наблюдаем и подменяем в тестах, не подменяя шину целиком.
/// </summary>
public interface IInboundMessagePublisher
{
    Task PublishAsync(
        string recipientQueue,
        InboundMessage message,
        CancellationToken cancellationToken = default);
}

/// <summary>Публикация в очередь получателя через шину Rebus.</summary>
public sealed class RebusInboundMessagePublisher(IBus bus) : IInboundMessagePublisher
{
    public Task PublishAsync(
        string recipientQueue,
        InboundMessage message,
        CancellationToken cancellationToken = default) =>
        bus.Advanced.Routing.Send(recipientQueue, message);
}
