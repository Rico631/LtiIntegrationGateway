namespace LtiIntegrationGateway.Infrastructure.Persistence.Entities.Flow;

/// <summary>
/// Состояние экземпляра сервиса потоков: какую версию конфигурации он применил, когда отчитался и
/// какие проблемы применения зафиксировал.
/// </summary>
public sealed class FlowInstanceState : IEntity
{
    /// <summary>Уникальный идентификатор записи.</summary>
    public Guid Id { get; set; }

    /// <summary>Идентификатор интеграции.</summary>
    public required string IntegrationId { get; set; }

    /// <summary>Идентификатор экземпляра (пода) внутри интеграции.</summary>
    public required string InstanceId { get; set; }

    /// <summary>Применённая версия конфигурации.</summary>
    public long AppliedVersion { get; set; }

    /// <summary>
    /// Признак обеспеченности активной конфигурации интеграции секретами: <c>true</c> — все ключи
    /// заполнены и материал сертификатов пригоден к использованию.
    /// </summary>
    public bool SecretConfigured { get; set; }

    /// <summary>
    /// Ближайший срок действия клиентского сертификата активной конфигурации интеграции; отсутствует,
    /// если активная конфигурация не использует клиентские сертификаты.
    /// </summary>
    public DateTimeOffset? CertificateExpiresAt { get; set; }

    /// <summary>
    /// Текст последней ошибки применения конфигурации; <c>null</c> — последнее применение успешно.
    /// </summary>
    public string? ApplyError { get; set; }

    /// <summary>Момент последнего отчёта в UTC; по нему вычисляется признак устаревания.</summary>
    public DateTimeOffset ReportedAt { get; set; }
}
