namespace LtiIntegrationGateway.Infrastructure.Persistence.Entities;

/// <summary>Признак доставки результата продукту.</summary>
public enum OutboundDeliveryStatus
{
    /// <summary>Результат зафиксирован, отправка ещё не подтверждена.</summary>
    Pending = 1,

    /// <summary>Результат отправлен продукту.</summary>
    Sent = 2,

    /// <summary>Результат не может быть доставлен: адрес не проверен или отсутствует.</summary>
    Undeliverable = 3,
}

/// <summary>Запись журнала исходящего сообщения: тела запроса и ответа для аудита и разбора инцидентов.</summary>
public sealed class OutboundMessageLog
{
    public Guid Id { get; set; }

    public required string MessageId { get; set; }

    public required string ProductId { get; set; }

    public required string ChannelName { get; set; }

    /// <summary>
    /// Адрес очереди, на который отправляется результат: значение <c>rbs2-return-address</c>,
    /// проверенное при приёме запроса. Пустая строка — адрес не был известен, запись не доставляется.
    /// </summary>
    public required string DeliveryAddress { get; set; }

    public required string HttpMethod { get; set; }

    public required string RelativePath { get; set; }

    /// <summary>Тело запроса в исходном виде.</summary>
    public byte[] RequestBody { get; set; } = [];

    /// <summary>Тело ответа внешней системы или пустой массив, если ответа не было.</summary>
    public byte[] ResponseBody { get; set; } = [];

    /// <summary>Тип содержимого ответа внешней системы; <c>null</c>, если ответа не было.</summary>
    public string? ContentType { get; set; }

    public int? HttpStatusCode { get; set; }

    public string? Outcome { get; set; }

    public string? ErrorCode { get; set; }

    /// <summary>Безопасное описание ошибки первого результата; <c>null</c>, если ошибки не было.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Номер активной ревизии канала на момент завершения операции (ADR §11.2); <c>null</c>, если
    /// отказ произошёл до загрузки ревизии.
    /// </summary>
    public int? ChannelRevisionNumber { get; set; }

    public int AttemptCount { get; set; }

    public OutboundDeliveryStatus DeliveryStatus { get; set; }

    /// <summary>
    /// Момент создания записи; в хранилище кодируется миллисекундами Unix, чтобы SQLite мог
    /// сортировать и индексировать значение.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }
}
