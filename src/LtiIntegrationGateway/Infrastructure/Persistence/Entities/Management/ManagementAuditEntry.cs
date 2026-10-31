namespace LtiIntegrationGateway.Infrastructure.Persistence.Entities.Management;

/// <summary>Аудит управляющей операции: фиксирует и успешные, и отклонённые вызовы API.</summary>
public sealed class ManagementAuditEntry : IEntity
{
    /// <summary>Уникальный идентификатор записи.</summary>
    public Guid Id { get; set; }

    /// <summary>Имя операции управляющего API, например <c>PublishChannelRevision</c>.</summary>
    public required string Operation { get; set; }

    /// <summary>Объект операции: имя канала, продукт, версия контракта.</summary>
    public required string Target { get; set; }

    /// <summary>Результат: <c>succeeded</c> или <c>rejected</c>.</summary>
    public required string Result { get; set; }

    /// <summary>Безопасные детали без значений секретов.</summary>
    public string? Details { get; set; }

    /// <summary>Момент события в UTC.</summary>
    public DateTimeOffset OccurredAt { get; set; }
}
