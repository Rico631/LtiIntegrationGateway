namespace LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;

/// <summary>Зарегистрированный исходящий канал интеграции с внешней системой.</summary>
public sealed class OutboundChannel : IEntity
{
    /// <summary>Уникальный идентификатор записи.</summary>
    public Guid Id { get; set; }

    /// <summary>Уникальное имя канала в пределах outbound-направления, например <c>bank-a</c>.</summary>
    public required string Name { get; set; }

    /// <summary>Момент создания записи в UTC.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Ревизии исходящего канала.</summary>
    public List<ChannelRevision> Revisions { get; set; } = [];
}
