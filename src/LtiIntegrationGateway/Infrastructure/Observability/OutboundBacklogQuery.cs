using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Outbound;

namespace LtiIntegrationGateway.Infrastructure.Observability;

/// <summary>Общая выборка результатов, которые должны быть доставлены продукту.</summary>
public static class OutboundBacklogQuery
{
    /// <summary>
    /// Возвращает <c>Pending</c>-записи с непустым адресом и существующей операцией.
    /// </summary>
    public static IQueryable<OutboundMessageLog> PendingForDelivery(this GatewayDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);

        return db.OutboundMessageLogs.Where(log =>
            log.DeliveryStatus == OutboundDeliveryStatus.Pending &&
            log.DeliveryAddress != string.Empty &&
            db.OutboundOperations.Any(operation =>
                operation.MessageId == log.MessageId &&
                operation.Status == OutboundOperationStatus.Completed));
    }
}
