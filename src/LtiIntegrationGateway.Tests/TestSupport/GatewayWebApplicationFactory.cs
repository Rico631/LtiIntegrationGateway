using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using TUnit.Core.Interfaces;

namespace LtiIntegrationGateway.Tests.TestSupport;

/// <summary>
/// Фабрика хоста шлюза для тестов: подменяет строку подключения на in-memory SQLite либо
/// собирает хост без неё, чтобы проверить fail-fast конфигурации.
/// </summary>
public sealed class GatewayWebApplicationFactory : WebApplicationFactory<Program>, IAsyncInitializer
{
    private readonly bool _supplyConnectionString;
    private readonly IReadOnlyDictionary<string, string?> _extraSettings;
    private readonly string? _connectionString;
    private readonly SqliteConnection? _keepAliveConnection;
    private readonly IHealthCheck? _extraReadinessCheck;

    private GatewayWebApplicationFactory(
        bool supplyConnectionString,
        IReadOnlyDictionary<string, string?>? extraSettings = null,
        string? connectionString = null,
        IHealthCheck? extraReadinessCheck = null)
    {
        _supplyConnectionString = supplyConnectionString;
        _extraSettings = extraSettings ?? new Dictionary<string, string?>();
        _extraReadinessCheck = extraReadinessCheck;

        if (!supplyConnectionString)
        {
            return;
        }

        if (connectionString is not null)
        {
            _connectionString = connectionString;

            return;
        }

        _connectionString = $"Data Source=file:gateway-tests-{Guid.NewGuid():N}?mode=memory&cache=shared";
        _keepAliveConnection = new SqliteConnection(_connectionString);
        _keepAliveConnection.Open();
    }

    public InMemoryLoggerProvider Logging { get; } = new();

    public static GatewayWebApplicationFactory WithStorage(IReadOnlyDictionary<string, string?>? settings = null) =>
        new(true, settings);

    /// <summary>Хост поверх заранее подготовленной базы: нужен для проверки fail-fast при старте.</summary>
    public static GatewayWebApplicationFactory WithExistingStorage(
        string connectionString,
        IReadOnlyDictionary<string, string?>? settings = null) =>
        new(true, settings, connectionString);

    public static GatewayWebApplicationFactory WithoutStorage() => new(false);

    /// <summary>Хост с дополнительной readiness-проверкой, которая всегда возвращает отказ.</summary>
    public static GatewayWebApplicationFactory WithUnhealthyReadiness(IHealthCheck check) =>
        new(true, extraReadinessCheck: check);

    public Task InitializeAsync()
    {
        // Сборка хоста запускается здесь, чтобы ошибка конфигурации возникала внутри теста.
        _ = Server;

        return Task.CompletedTask;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.Sources.Clear();

            var settings = new Dictionary<string, string?>();

            if (_supplyConnectionString)
            {
                settings["ConnectionStrings:Gateway"] = _connectionString;
            }

            // Фоновая служба доставки по умолчанию выключена: тесты вызывают DispatchPendingAsync
            // напрямую и не должны зависеть от таймера.
            settings["Outbound:DeliveryDispatcher:Enabled"] = "false";

            foreach (var pair in _extraSettings)
            {
                settings[pair.Key] = pair.Value;
            }

            configuration.AddInMemoryCollection(settings);
        });

        builder.ConfigureLogging(logging => logging.AddProvider(Logging));

        if (_extraReadinessCheck is not null)
        {
            builder.ConfigureServices(services => services
                .AddHealthChecks()
                .AddCheck("test-unhealthy", _extraReadinessCheck, tags: ["ready"]));
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _keepAliveConnection?.Dispose();
        }
    }
}
