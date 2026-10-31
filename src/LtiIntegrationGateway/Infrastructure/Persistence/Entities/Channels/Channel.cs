namespace LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;

/// <summary>Зарегистрированный канал интеграции с внешней системой.</summary>
public sealed class Channel : IEntity
{
    /// <summary>Уникальный идентификатор записи.</summary>
    public Guid Id { get; set; }

    /// <summary>Уникальное имя канала, например <c>bank-a</c>.</summary>
    public required string Name { get; set; }

    /// <summary>Момент создания записи в UTC.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Ревизии канала.</summary>
    public List<ChannelRevision> Revisions { get; set; } = [];
}
