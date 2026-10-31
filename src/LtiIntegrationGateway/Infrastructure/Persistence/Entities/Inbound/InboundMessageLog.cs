namespace LtiIntegrationGateway.Infrastructure.Persistence.Entities.Inbound;

/// <summary>Статус передачи входящего сообщения продукту.</summary>
public enum InboundDeliveryStatus
{
    /// <summary>Сообщение принято, передача ещё не подтверждена.</summary>
    Pending = 1,

    /// <summary>Сообщение передано в очередь получателя.</summary>
    Sent = 2,

    /// <summary>Передать сообщение не удалось.</summary>
    Failed = 3,
}

/// <summary>Журнал входящих вызовов: тело, маршрут, операция, статус передачи.</summary>
public sealed class InboundMessageLog : IEntity
{
    /// <summary>Уникальный идентификатор записи.</summary>
    public Guid Id { get; set; }

    /// <summary>Идентификатор сообщения, назначенный шлюзом.</summary>
    public required string MessageId { get; set; }

    /// <summary>Имя канала.</summary>
    public required string ChannelName { get; set; }

    /// <summary>Идентификатор версии контракта.</summary>
    public Guid? ContractVersionId { get; set; }

    /// <summary>HTTP-метод запроса.</summary>
    public required string HttpMethod { get; set; }

    /// <summary>Фактический относительный путь без query string и без percent-декодирования.</summary>
    public required string RelativePath { get; set; }

    /// <summary>Шаблон маршрута из OpenAPI.</summary>
    public string? RouteTemplate { get; set; }

    /// <summary>Идентификатор операции из OpenAPI.</summary>
    public string? OperationId { get; set; }

    /// <summary>Тип содержимого сообщения.</summary>
    public string? ContentType { get; set; }

    /// <summary>Тело запроса в исходном виде.</summary>
    public byte[] RequestBody { get; set; } = [];

    /// <summary>Query-параметры с сохранением порядка и повторов в виде JSON-массива.</summary>
    public string? QueryParameters { get; set; }

    /// <summary>Состояние доставки.</summary>
    public InboundDeliveryStatus DeliveryStatus { get; set; }

    /// <summary>Код ошибки при отклонении вызова; пусто для принятого вызова.</summary>
    public string? ErrorCode { get; set; }

    /// <summary>
    /// Момент приёма вызова; в хранилище кодируется миллисекундами Unix, чтобы SQLite мог
    /// сортировать и индексировать значение.
    /// </summary>
    public DateTimeOffset ReceivedAt { get; set; }
}
