using LtiIntegrationGateway.Features.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LtiIntegrationGateway.Tests.TestSupport;

/// <summary>
/// Пара хостов над одной базой: конфигурация задаётся через сервис управления, проверки —
/// через сервис потоков, как в эксплуатации.
/// </summary>
public sealed class GatewayPair : IDisposable
{
    public GatewayPair(
        IReadOnlyDictionary<string, string?>? managementSettings = null,
        IReadOnlyDictionary<string, string?>? flowSettings = null)
    {
        Database = new PostgresTestDatabase();
        Database.Migrate();
        Management = ManagementHostFactory.WithExistingStorage(Database.ConnectionString, managementSettings);
        Flow = FlowHostFactory.WithExistingStorage(Database.ConnectionString, flowSettings);
    }

    public PostgresTestDatabase Database { get; }

    public ManagementHostFactory Management { get; }

    public FlowHostFactory Flow { get; }

    /// <summary>Применяет конфигурацию в сервисе потоков, не дожидаясь опроса.</summary>
    public Task ApplyAsync() =>
        Flow.Services.GetRequiredService<FlowConfigurationApplier>().ApplyAsync();

    public void Dispose()
    {
        Flow.Dispose();
        Management.Dispose();
        Database.Dispose();
    }
}
