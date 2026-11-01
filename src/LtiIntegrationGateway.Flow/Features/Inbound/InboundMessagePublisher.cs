using LtiIntegrationGateway.Contracts.Messages.V1;
using LtiIntegrationGateway.Features.Configuration;
using LtiIntegrationGateway.Infrastructure.Rebus;
using Microsoft.Extensions.Options;
using Rebus.Bus;
using Rebus.Messages;

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

/// <summary>
/// Публикация и request/reply через шину Rebus.
/// </summary>
/// <remarks>
/// В синхронном режиме адрес возврата — reply-очередь экземпляра, а не общая входная очередь
/// интеграции: при нескольких репликах ответ продукта получает именно экземпляр, принявший вызов.
/// </remarks>
public sealed class RebusInboundMessagePublisher(
    IBus bus,
    IOptions<FlowOptions> options,
    PendingInboundRequests pending) : IInboundMessagePublisher
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
        var flow = options.Value;
        var replyQueue = QueueNames.ReplyQueue(flow.Integration, flow.InstanceId);

        var correlationId = Guid.NewGuid().ToString("N");
        using var request = pending.Register(correlationId);

        var headers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Headers.ReturnAddress] = replyQueue,
            // Продукт обязан вернуть назначенный шлюзом message_id; несовпадающий идентификатор
            // проверяется в обработчике и приводит к 502, поэтому ожидание привязано к корреляции.
            [Headers.CorrelationId] = correlationId,
        };

        await bus.Advanced.Routing.Send(recipientQueue, message, headers);

        // Таймаут и отмена завершают ожидание детерминированно; поздний ответ не находит ожидания.
        return await request.Completion.WaitAsync(timeout, cancellationToken);
    }
}
