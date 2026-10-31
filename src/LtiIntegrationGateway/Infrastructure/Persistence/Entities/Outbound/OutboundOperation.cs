namespace LtiIntegrationGateway.Infrastructure.Persistence.Entities.Outbound;

/// <summary>Состояние исходящей операции.</summary>
public enum OutboundOperationStatus
{
    /// <summary>Операция принята в обработку.</summary>
    Accepted = 1,

    /// <summary>Выполняется внешний вызов.</summary>
    InProgress = 2,

    /// <summary>Результат получен и зафиксирован.</summary>
    Completed = 3,
}

/// <summary>Состояние исходящей операции, идентифицируемой <c>message_id</c>.</summary>
public sealed class OutboundOperation : IEntity
{
    /// <summary>Уникальный идентификатор записи.</summary>
    public Guid Id { get; set; }

    /// <summary>Идентификатор операции из сообщения; уникален.</summary>
    public required string MessageId { get; set; }

    /// <summary>Идентификатор продукта.</summary>
    public required string ProductId { get; set; }

    /// <summary>Имя канала.</summary>
    public required string ChannelName { get; set; }

    /// <summary>Состояние записи.</summary>
    public OutboundOperationStatus Status { get; set; }

    /// <summary>Исход вызова: <c>HTTP_RESPONSE</c>, <c>TRANSPORT_ERROR</c> или <c>GATEWAY_REFUSED</c>.</summary>
    public string? Outcome { get; set; }

    /// <summary>HTTP-статус ответа.</summary>
    public int? HttpStatusCode { get; set; }

    /// <summary>Код ошибки.</summary>
    public string? ErrorCode { get; set; }

    /// <summary>
    /// Идентификатор бизнес-процесса первого запроса: сохраняется, чтобы повторный результат был
    /// идентичен первому.
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>Количество выполненных попыток.</summary>
    public int AttemptCount { get; set; }

    /// <summary>Адрес доставки результата, проверенный по маршруту продукта.</summary>
    public string? ReturnAddress { get; set; }

    /// <summary>Момент создания записи в UTC.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Момент завершения операции в UTC.</summary>
    public DateTimeOffset? CompletedAt { get; set; }
}
