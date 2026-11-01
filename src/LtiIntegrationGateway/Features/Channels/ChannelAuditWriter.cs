using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;

namespace LtiIntegrationGateway.Features.Channels;

/// <summary>Запись аудита изменения конфигурации: пишется в той же транзакции, что и изменение.</summary>
internal static class ChannelAuditWriter
{
    public static void Add(
        GatewayDbContext db,
        Guid channelId,
        int? revisionNumber,
        string operation,
        string? details = null,
        ChannelDirection direction = ChannelDirection.Outbound)
    {
        db.ChannelAuditEntries.Add(new ChannelAuditEntry
        {
            Id = Guid.NewGuid(),
            ChannelId = channelId,
            Direction = direction,
            RevisionNumber = revisionNumber,
            Operation = operation,
            OccurredAt = DateTimeOffset.UtcNow,
            Details = details,
        });
    }
}
