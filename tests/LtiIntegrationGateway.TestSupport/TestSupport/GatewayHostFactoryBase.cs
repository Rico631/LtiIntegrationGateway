using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using TUnit.Core.Interfaces;

namespace LtiIntegrationGateway.Tests.TestSupport;

/// <summary>
/// Общая обвязка тестового хоста: подменяет строку подключения на отдельную базу PostgreSQL либо
/// собирает хост без неё, чтобы проверить fail-fast конфигурации.
/// </summary>
public abstract class GatewayHostFactoryBase<TEntryPoint> : WebApplicationFactory<TEntryPoint>, IAsyncInitializer
    where TEntryPoint : class
{
    private readonly bool _supplyConnectionString;
    private readonly IReadOnlyDictionary<string, string?> _extraSettings;
    private readonly string? _connectionString;
    private readonly PostgresTestDatabase? _database;
    private readonly IHealthCheck? _extraReadinessCheck;

    protected GatewayHostFactoryBase(
        bool supplyConnectionString,
        IReadOnlyDictionary<string, string?>? extraSettings = null,
        string? connectionString = null,
        IHealthCheck? extraReadinessCheck = null)
    {
        _supplyConnectionString = supplyConnectionString;
        _extraSettings = extraSettings ?? new Dictionary<string, string?>();
        _extraReadinessCheck = extraReadinessCheck;

        if (!supplyConnectionString || connectionString is not null)
        {
            _connectionString = connectionString;

            return;
        }

        _database = new PostgresTestDatabase();
        _connectionString = _database.ConnectionString;
    }

    public InMemoryLoggerProvider Logging { get; } = new();

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

            ConfigureSettings(settings);

            configuration.AddInMemoryCollection(settings);
        });

        builder.ConfigureLogging(logging => logging.AddProvider(Logging));

        ConfigureHostServices(builder);

        if (_extraReadinessCheck is not null)
        {
            builder.ConfigureServices(services => services
                .AddHealthChecks()
                .AddCheck("test-unhealthy", _extraReadinessCheck, tags: ["ready"]));
        }
    }

    /// <summary>Дополнительные настройки конкретного сервиса.</summary>
    protected virtual void ConfigureSettings(IDictionary<string, string?> settings)
    {
    }

    /// <summary>Дополнительные сервисы конкретного сервиса.</summary>
    protected virtual void ConfigureHostServices(IWebHostBuilder builder)
    {
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _database?.Dispose();
        }
    }
}
