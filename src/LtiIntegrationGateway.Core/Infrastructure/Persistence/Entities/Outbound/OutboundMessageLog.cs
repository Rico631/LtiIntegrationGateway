namespace LtiIntegrationGateway.Infrastructure.Persistence.Entities.Outbound;

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
public sealed class OutboundMessageLog : IEntity
{
    /// <summary>Уникальный идентификатор записи.</summary>
    public Guid Id { get; set; }

    /// <summary>Интеграция, в пространстве которой выполнен вызов.</summary>
    public string IntegrationId { get; set; } = IntegrationDefaults.Default;

    /// <summary>Идентификатор сообщения.</summary>
    public required string MessageId { get; set; }

    /// <summary>Идентификатор продукта.</summary>
    public required string ProductId { get; set; }

    /// <summary>Имя канала.</summary>
    public required string ChannelName { get; set; }

    /// <summary>
    /// Адрес очереди, на который отправляется результат: значение <c>rbs2-return-address</c>,
    /// проверенное при приёме запроса. Пустая строка — адрес не был известен, запись не доставляется.
    /// </summary>
    public required string DeliveryAddress { get; set; }

    /// <summary>HTTP-метод запроса.</summary>
    public required string HttpMethod { get; set; }

    /// <summary>Относительный путь запроса.</summary>
    public required string RelativePath { get; set; }

    /// <summary>Тело запроса в исходном виде.</summary>
    public byte[] RequestBody { get; set; } = [];

    /// <summary>Тело ответа внешней системы или пустой массив, если ответа не было.</summary>
    public byte[] ResponseBody { get; set; } = [];

    /// <summary>Тип содержимого ответа внешней системы; <c>null</c>, если ответа не было.</summary>
    public string? ContentType { get; set; }

    /// <summary>HTTP-статус ответа.</summary>
    public int? HttpStatusCode { get; set; }

    /// <summary>Исход вызова.</summary>
    public string? Outcome { get; set; }

    /// <summary>Код ошибки.</summary>
    public string? ErrorCode { get; set; }

    /// <summary>Безопасное описание ошибки первого результата; <c>null</c>, если ошибки не было.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Номер активной ревизии канала на момент завершения операции (ADR §11.2); <c>null</c>, если
    /// отказ произошёл до загрузки ревизии.
    /// </summary>
    public int? ChannelRevisionNumber { get; set; }

    /// <summary>Количество выполненных попыток.</summary>
    public int AttemptCount { get; set; }

    /// <summary>Состояние доставки.</summary>
    public OutboundDeliveryStatus DeliveryStatus { get; set; }

    /// <summary>Момент создания записи в UTC.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
