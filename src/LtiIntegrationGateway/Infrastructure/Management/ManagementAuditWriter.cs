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
/// <remarks>
/// Объект и детали усекаются до длин модели здесь, поэтому вызывающие срезы не дублируют эту проверку.
/// </remarks>
public sealed class ManagementAuditWriter(GatewayDbContext db)
{
    /// <summary>Максимальная длина объекта операции в модели аудита.</summary>
    public const int MaxTargetLength = 256;

    /// <summary>Максимальная длина деталей записи в модели аудита.</summary>
    public const int MaxDetailsLength = 2048;

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
            Target = Truncate(target, MaxTargetLength)!,
            Result = succeeded ? ManagementAuditResults.Succeeded : ManagementAuditResults.Rejected,
            Details = Truncate(details, MaxDetailsLength),
            OccurredAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}
