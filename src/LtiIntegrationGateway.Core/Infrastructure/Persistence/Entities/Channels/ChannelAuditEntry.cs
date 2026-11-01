namespace LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;

/// <summary>Запись аудита изменения конфигурации канала.</summary>
public sealed class ChannelAuditEntry : IEntity
{
    /// <summary>Уникальный идентификатор записи.</summary>
    public Guid Id { get; set; }

    /// <summary>Направление канала, которому принадлежит запись.</summary>
    public ChannelDirection Direction { get; set; }

    /// <summary>Идентификатор канала.</summary>
    public Guid ChannelId { get; set; }

    /// <summary>Номер ревизии, к которой относится операция, если применимо.</summary>
    public int? RevisionNumber { get; set; }

    /// <summary>Имя операции среза, например <c>publish-channel-revision</c>.</summary>
    public required string Operation { get; set; }

    /// <summary>Момент события в UTC.</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Краткое описание изменения без секретов.</summary>
    public string? Details { get; set; }
}
