using System.Collections.Concurrent;
using LtiIntegrationGateway.Contracts.Messages.V1;
using Rebus.Handlers;
using Rebus.Messages;
using Rebus.Pipeline;

namespace LtiIntegrationGateway.Features.Inbound;

/// <summary>
/// Ожидания ответа продукта в синхронном режиме. Экземпляр регистрирует ожидание по корреляции
/// запроса, а обработчик reply-очереди завершает именно его.
/// </summary>
/// <remarks>
/// Корреляция берётся из заголовка <c>rbs2-corr-id</c>, а не из тела ответа: продукт обязан вернуть
/// назначенный шлюзом <c>message_id</c>, но несовпадающий идентификатор — это некорректный ответ
/// (<c>502</c>), а не потерянное ожидание. Поздний ответ после таймаута не находит ожидания и
/// игнорируется: запись журнала, зафиксированная таймаутом, не меняется. Ответ, пришедший на
/// reply-очередь другого экземпляра, этот экземпляр не видит.
/// </remarks>
public sealed class PendingInboundRequests
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<InboundResponse>> _pending =
        new(StringComparer.Ordinal);

    /// <summary>Регистрирует ожидание ответа по идентификатору корреляции.</summary>
    public PendingInboundRequest Register(string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        var completion = new TaskCompletionSource<InboundResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        if (!_pending.TryAdd(correlationId, completion))
        {
            throw new InvalidOperationException(
                $"Ожидание ответа '{correlationId}' уже зарегистрировано.");
        }

        return new PendingInboundRequest(this, correlationId, completion);
    }

    /// <summary>
    /// Завершает ожидание ответом продукта. Возвращает <c>false</c>, если корреляция неизвестна
    /// (поздний ответ или ответ другому экземпляру).
    /// </summary>
    public bool TryComplete(string? correlationId, InboundResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (string.IsNullOrWhiteSpace(correlationId) ||
            !_pending.TryRemove(correlationId, out var completion))
        {
            return false;
        }

        return completion.TrySetResult(response);
    }

    internal bool Remove(string correlationId) => _pending.TryRemove(correlationId, out _);
}

/// <summary>Зарегистрированное ожидание; освобождается при завершении, таймауте или отмене.</summary>
public sealed class PendingInboundRequest : IDisposable
{
    private readonly PendingInboundRequests _owner;
    private readonly string _correlationId;

    internal PendingInboundRequest(
        PendingInboundRequests owner,
        string correlationId,
        TaskCompletionSource<InboundResponse> completion)
    {
        _owner = owner;
        _correlationId = correlationId;
        Completion = completion.Task;
    }

    /// <summary>Задача, завершаемая ответом продукта.</summary>
    public Task<InboundResponse> Completion { get; }

    /// <inheritdoc />
    public void Dispose() => _owner.Remove(_correlationId);
}

/// <summary>Обработчик reply-очереди: завершает ожидание экземпляра, принявшего вызов.</summary>
public sealed class InboundResponseHandler(
    PendingInboundRequests pending,
    ILogger<InboundResponseHandler> logger) : IHandleMessages<InboundResponse>
{
    /// <inheritdoc />
    public Task Handle(InboundResponse message)
    {
        var correlationId = MessageContext.Current?.Headers
            .GetValueOrDefault(Headers.CorrelationId);

        if (!pending.TryComplete(correlationId, message))
        {
            // Поздний ответ, ответ без корреляции или ответ экземпляру без ожидания.
            logger.LogDebug(
                "ответ на сообщение {MessageId} не найден среди ожиданий экземпляра",
                message.MessageId);
        }

        return Task.CompletedTask;
    }
}
