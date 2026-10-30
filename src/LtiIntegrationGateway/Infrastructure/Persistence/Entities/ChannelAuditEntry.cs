namespace LtiIntegrationGateway.Infrastructure.Persistence.Entities;

/// <summary>Запись аудита изменения конфигурации канала.</summary>
public sealed class ChannelAuditEntry
{
    public Guid Id { get; set; }

    public Guid ChannelId { get; set; }

    /// <summary>Номер ревизии, к которой относится операция, если применимо.</summary>
    public int? RevisionNumber { get; set; }

    /// <summary>Имя операции среза, например <c>publish-channel-revision</c>.</summary>
    public required string Operation { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Краткое описание изменения без секретов.</summary>
    public string? Details { get; set; }
}
