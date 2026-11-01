using LtiIntegrationGateway.Contracts.Messages.V1;
using Rebus;
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

    Task<InboundResponse> SendRequestAsync(
        string recipientQueue,
        InboundMessage message,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

/// <summary>Публикация и request/reply через шину Rebus.</summary>
public sealed class RebusInboundMessagePublisher(IBus bus) : IInboundMessagePublisher
{
    public Task PublishAsync(
        string recipientQueue,
        InboundMessage message,
        CancellationToken cancellationToken = default) =>
        bus.Advanced.Routing.Send(recipientQueue, message);

    public async Task<InboundResponse> SendRequestAsync(
        string recipientQueue,
        InboundMessage message,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var request = bus.Advanced.Routing.SendRequest<InboundResponse>(
            recipientQueue,
            message,
            new Dictionary<string, string>(StringComparer.Ordinal),
            timeout,
            cancellationToken);

        // Rebus.Async in the in-memory transport does not always complete the pending
        // request when its cancellation token fires. The HTTP contract requires a
        // deterministic timeout/cancellation, so the wrapper enforces both.
        return await request.WaitAsync(timeout, cancellationToken);
    }
}
