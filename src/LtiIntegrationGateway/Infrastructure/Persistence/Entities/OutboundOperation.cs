namespace LtiIntegrationGateway.Infrastructure.Persistence.Entities;

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
public sealed class OutboundOperation
{
    public Guid Id { get; set; }

    /// <summary>Идентификатор операции из сообщения; уникален.</summary>
    public required string MessageId { get; set; }

    public required string ProductId { get; set; }

    public required string ChannelName { get; set; }

    public OutboundOperationStatus Status { get; set; }

    /// <summary>Исход вызова: <c>HTTP_RESPONSE</c>, <c>TRANSPORT_ERROR</c> или <c>GATEWAY_REFUSED</c>.</summary>
    public string? Outcome { get; set; }

    public int? HttpStatusCode { get; set; }

    public string? ErrorCode { get; set; }

    public int AttemptCount { get; set; }

    /// <summary>Адрес доставки результата, проверенный по маршруту продукта.</summary>
    public string? ReturnAddress { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
}
