using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using TUnit.Core.Interfaces;

namespace LtiIntegrationGateway.Tests.TestSupport;

/// <summary>
/// Тестовый endpoint создаёт пустой входящий канал без контракта: сценарии реестра контрактов
/// затем загружают первую версию обычным management-вызовом.
/// </summary>
internal sealed class TestInboundChannelEndpointFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, nextMiddleware) =>
        {
            if (HttpMethods.IsPost(context.Request.Method) &&
                context.Request.Path == "/test/inbound-channels")
            {
                var name = context.Request.Query["name"].ToString();
                var db = context.RequestServices.GetRequiredService<GatewayDbContext>();

                if (!await db.InboundChannels.AnyAsync(channel => channel.Name == name))
                {
                    db.InboundChannels.Add(new InboundChannel
                    {
                        Id = Guid.NewGuid(),
                        Name = name,
                        ResponseMode = InboundResponseMode.Async,
                        InboundResponseTimeoutMs = 30_000,
                        CreatedAt = DateTimeOffset.UtcNow,
                    });

                    await db.SaveChangesAsync();
                }

                context.Response.StatusCode = StatusCodes.Status200OK;
                return;
            }

            await nextMiddleware(context);
        });

        next(app);
    };
}


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

        builder.ConfigureServices(services => services
            .AddSingleton<IStartupFilter, TestInboundChannelEndpointFilter>());

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
