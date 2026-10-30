using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
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
    private readonly string? _connectionString;
    private readonly SqliteConnection? _keepAliveConnection;

    private GatewayWebApplicationFactory(bool supplyConnectionString)
    {
        _supplyConnectionString = supplyConnectionString;

        if (!supplyConnectionString)
        {
            return;
        }

        _connectionString = $"Data Source=file:gateway-tests-{Guid.NewGuid():N}?mode=memory&cache=shared";
        _keepAliveConnection = new SqliteConnection(_connectionString);
        _keepAliveConnection.Open();
    }

    public InMemoryLoggerProvider Logging { get; } = new();

    public static GatewayWebApplicationFactory WithStorage() => new(true);

    public static GatewayWebApplicationFactory WithoutStorage() => new(false);

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

            configuration.AddInMemoryCollection(settings);
        });

        builder.ConfigureLogging(logging => logging.AddProvider(Logging));
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
