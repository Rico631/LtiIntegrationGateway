using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities;

namespace LtiIntegrationGateway.Infrastructure.Management;

/// <summary>Значения результата управляющей операции в аудите.</summary>
public static class ManagementAuditResults
{
    public const string Succeeded = "succeeded";
    public const string Rejected = "rejected";
}

/// <summary>Запись аудита управляющей операции; значения секретов в детали не попадают.</summary>
public sealed class ManagementAuditWriter(GatewayDbContext db)
{
    public async Task RecordAsync(
        string operation,
        string target,
        bool succeeded,
        string? details = null,
        CancellationToken cancellationToken = default)
    {
        db.ManagementAuditEntries.Add(new ManagementAuditEntry
        {
            Id = Guid.NewGuid(),
            Operation = operation,
            Target = target,
            Result = succeeded ? ManagementAuditResults.Succeeded : ManagementAuditResults.Rejected,
            Details = details,
            OccurredAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(cancellationToken);
    }
}
