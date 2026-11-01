using LtiIntegrationGateway.Management;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LtiIntegrationGateway.Tests.TestSupport;

/// <summary>Хост сервиса управления.</summary>
public sealed class ManagementHostFactory : GatewayHostFactoryBase<ManagementHost>
{
    private ManagementHostFactory(
        bool supplyConnectionString,
        IReadOnlyDictionary<string, string?>? extraSettings = null,
        string? connectionString = null,
        IHealthCheck? extraReadinessCheck = null)
        : base(supplyConnectionString, extraSettings, connectionString, extraReadinessCheck)
    {
    }

    public static ManagementHostFactory WithStorage(IReadOnlyDictionary<string, string?>? settings = null) =>
        new(true, settings);

    /// <summary>Хост поверх заранее подготовленной базы: нужен для проверки fail-fast при старте.</summary>
    public static ManagementHostFactory WithExistingStorage(
        string connectionString,
        IReadOnlyDictionary<string, string?>? settings = null) =>
        new(true, settings, connectionString);

    public static ManagementHostFactory WithoutStorage() => new(false);

    /// <summary>Хост с дополнительной readiness-проверкой, которая всегда возвращает отказ.</summary>
    public static ManagementHostFactory WithUnhealthyReadiness(IHealthCheck check) =>
        new(true, extraReadinessCheck: check);

    protected override void ConfigureHostServices(IWebHostBuilder builder) =>
        builder.ConfigureServices(services => services
            .AddSingleton<IStartupFilter, TestInboundChannelEndpointFilter>());
}
