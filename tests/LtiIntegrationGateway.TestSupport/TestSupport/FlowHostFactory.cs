using LtiIntegrationGateway.Flow;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LtiIntegrationGateway.Tests.TestSupport;

/// <summary>Хост сервиса потоков.</summary>
public sealed class FlowHostFactory : GatewayHostFactoryBase<FlowHost>
{
    /// <summary>Интеграция тестового хоста по умолчанию; тест может переопределить её настройкой.</summary>
    public const string DefaultIntegration = "default";

    private FlowHostFactory(
        bool supplyConnectionString,
        IReadOnlyDictionary<string, string?>? extraSettings = null,
        string? connectionString = null,
        IHealthCheck? extraReadinessCheck = null)
        : base(supplyConnectionString, extraSettings, connectionString, extraReadinessCheck)
    {
    }

    /// <summary>
    /// Экземпляр без заданной интеграции не стартует, поэтому тестовый хост явно называет её; явное
    /// значение из настроек теста имеет приоритет.
    /// </summary>
    protected override void ConfigureSettings(IDictionary<string, string?> settings)
    {
        if (!settings.ContainsKey("Gateway:Integration"))
        {
            settings["Gateway:Integration"] = DefaultIntegration;
        }
    }

    public static FlowHostFactory WithStorage(IReadOnlyDictionary<string, string?>? settings = null) =>
        new(true, settings);

    public static FlowHostFactory WithExistingStorage(
        string connectionString,
        IReadOnlyDictionary<string, string?>? settings = null) =>
        new(true, settings, connectionString);

    public static FlowHostFactory WithoutStorage() => new(false);

    public static FlowHostFactory WithUnhealthyReadiness(IHealthCheck check) =>
        new(true, extraReadinessCheck: check);
}
