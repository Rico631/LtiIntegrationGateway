namespace LtiIntegrationGateway.Infrastructure.Persistence.Entities;

/// <summary>Зарегистрированный канал интеграции с внешней системой.</summary>
public sealed class Channel
{
    public Guid Id { get; set; }

    /// <summary>Уникальное имя канала, например <c>bank-a</c>.</summary>
    public required string Name { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public List<ChannelRevision> Revisions { get; set; } = [];
}
