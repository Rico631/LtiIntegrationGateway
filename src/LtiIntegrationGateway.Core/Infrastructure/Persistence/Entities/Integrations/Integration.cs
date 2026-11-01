namespace LtiIntegrationGateway.Infrastructure.Persistence.Entities.Integrations;

/// <summary>
/// Интеграция: единица конфигурации и изоляции. Экземпляр сервиса потоков обслуживает ровно одну
/// интеграцию, а каналы, контракты и маршруты продуктов принадлежат только ей.
/// </summary>
public sealed class Integration : IEntity
{
    /// <summary>Уникальный идентификатор записи.</summary>
    public Guid Id { get; set; }

    /// <summary>Идентификатор интеграции в формате <c>^[a-z][a-z0-9-]{1,31}$</c>; он же имя.</summary>
    public required string IntegrationId { get; set; }

    /// <summary>Монотонная версия действующей конфигурации; увеличивается в транзакции изменения.</summary>
    public long ConfigurationVersion { get; set; }

    /// <summary>Момент появления записи в UTC.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
