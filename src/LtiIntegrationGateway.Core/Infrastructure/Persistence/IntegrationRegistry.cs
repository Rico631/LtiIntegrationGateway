using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Integrations;
using Microsoft.EntityFrameworkCore;

namespace LtiIntegrationGateway.Infrastructure.Persistence;

/// <summary>
/// Реестр интеграций: гарантирует существование записи и ведёт версию действующей конфигурации.
/// </summary>
/// <remarks>
/// Отдельного административного API у интеграции нет (решение 1.4): запись создаётся при первой
/// операции над её ресурсами, а версия увеличивается в транзакции изменения.
/// </remarks>
public static class IntegrationRegistry
{
    /// <summary>Возвращает существующую запись интеграции или создаёт её.</summary>
    public static async Task<Integration> GetOrCreateAsync(
        GatewayDbContext db,
        string integrationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentException.ThrowIfNullOrWhiteSpace(integrationId);

        var existing = await db.Integrations
            .SingleOrDefaultAsync(entity => entity.IntegrationId == integrationId, cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var created = new Integration
        {
            IntegrationId = integrationId,
            ConfigurationVersion = 0,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.Integrations.Add(created);

        return created;
    }

    /// <summary>Увеличивает версию действующей конфигурации; вызывается перед сохранением изменений.</summary>
    public static async Task<long> BumpAsync(
        GatewayDbContext db,
        string integrationId,
        CancellationToken cancellationToken = default)
    {
        var integration = await GetOrCreateAsync(db, integrationId, cancellationToken);

        integration.ConfigurationVersion++;

        return integration.ConfigurationVersion;
    }
}
