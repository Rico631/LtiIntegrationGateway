using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Outbound;

namespace LtiIntegrationGateway.Infrastructure.Observability;

/// <summary>Выборка результатов интеграции, которые должны быть доставлены продукту.</summary>
public static class OutboundBacklogQuery
{
    /// <summary>
    /// Возвращает <c>Pending</c>-записи интеграции с непустым адресом и существующей операцией той же
    /// интеграции.
    /// </summary>
    public static IQueryable<OutboundMessageLog> PendingForDelivery(
        this GatewayDbContext db,
        string integrationId)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentException.ThrowIfNullOrWhiteSpace(integrationId);

        return db.OutboundMessageLogs.Where(log =>
            log.IntegrationId == integrationId &&
            log.DeliveryStatus == OutboundDeliveryStatus.Pending &&
            log.DeliveryAddress != string.Empty &&
            db.OutboundOperations.Any(operation =>
                operation.IntegrationId == log.IntegrationId &&
                operation.MessageId == log.MessageId &&
                operation.Status == OutboundOperationStatus.Completed));
    }
}
