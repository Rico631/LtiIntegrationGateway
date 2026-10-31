namespace LtiIntegrationGateway.Infrastructure.Persistence.Entities;

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
public sealed class InboundMessageLog
{
    public Guid Id { get; set; }

    /// <summary>Идентификатор сообщения, назначенный шлюзом.</summary>
    public required string MessageId { get; set; }

    public required string ChannelName { get; set; }

    public Guid? ContractVersionId { get; set; }

    public required string HttpMethod { get; set; }

    /// <summary>Фактический относительный путь без query string и без percent-декодирования.</summary>
    public required string RelativePath { get; set; }

    public string? RouteTemplate { get; set; }

    public string? OperationId { get; set; }

    public string? ContentType { get; set; }

    /// <summary>Тело запроса в исходном виде.</summary>
    public byte[] RequestBody { get; set; } = [];

    /// <summary>Query-параметры с сохранением порядка и повторов в виде JSON-массива.</summary>
    public string? QueryParameters { get; set; }

    public InboundDeliveryStatus DeliveryStatus { get; set; }

    /// <summary>Код ошибки при отклонении вызова; пусто для принятого вызова.</summary>
    public string? ErrorCode { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }

    /// <summary>
    /// Момент приёма вызова в миллисекундах Unix: по нему строятся выборки «последние ошибки» в
    /// порядке времени. Отдельная колонка нужна потому, что SQLite не поддерживает сортировку
    /// по <see cref="DateTimeOffset"/>.
    /// </summary>
    public long ReceivedAtUnixMs { get; set; }
}
