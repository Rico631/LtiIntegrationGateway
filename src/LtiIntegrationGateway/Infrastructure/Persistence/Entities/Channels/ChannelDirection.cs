namespace LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;

/// <summary>Направление канала, к которому относится запись аудита.</summary>
public enum ChannelDirection
{
    /// <summary>Исходящий канал.</summary>
    Outbound = 1,

    /// <summary>Входящий канал.</summary>
    Inbound = 2,
}
