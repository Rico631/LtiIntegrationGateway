namespace LtiIntegrationGateway.Infrastructure.Persistence.Entities;

/// <summary>Признак доставки результата продукту.</summary>
public enum OutboundDeliveryStatus
{
    /// <summary>Результат зафиксирован, отправка ещё не подтверждена.</summary>
    Pending = 1,

    /// <summary>Результат отправлен продукту.</summary>
    Sent = 2,
}

/// <summary>Запись журнала исходящего сообщения: тела запроса и ответа для аудита и разбора инцидентов.</summary>
public sealed class OutboundMessageLog
{
    public Guid Id { get; set; }

    public required string MessageId { get; set; }

    public required string ProductId { get; set; }

    public required string ChannelName { get; set; }

    public required string HttpMethod { get; set; }

    public required string RelativePath { get; set; }

    /// <summary>Тело запроса в исходном виде.</summary>
    public byte[] RequestBody { get; set; } = [];

    /// <summary>Тело ответа внешней системы или пустой массив, если ответа не было.</summary>
    public byte[] ResponseBody { get; set; } = [];

    public string? ContentType { get; set; }

    public int? HttpStatusCode { get; set; }

    public string? Outcome { get; set; }

    public string? ErrorCode { get; set; }

    public int AttemptCount { get; set; }

    public OutboundDeliveryStatus DeliveryStatus { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
