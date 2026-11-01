using LtiIntegrationGateway.Infrastructure.Health;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LtiIntegrationGateway.Tests.Infrastructure.Health;

/// <summary>Проверка readiness-пробы хранилища.</summary>
public class DatabaseHealthCheckTests
{
    [Test]
    public async Task Available_database_is_healthy()
    {
        using var database = new PostgresTestDatabase();
        database.Migrate();

        var services = new ServiceCollection();
        services.AddScoped(_ => database.CreateContext());

        using var provider = services.BuildServiceProvider();
        var check = new DatabaseHealthCheck(provider.GetRequiredService<IServiceScopeFactory>());

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
        await Assert.That(result.Description).DoesNotContain("Data Source");
    }

    [Test]
    public async Task Unavailable_database_is_unhealthy_without_connection_string()
    {
        // Недоступный адрес: проверка обязана вернуть Unhealthy и не раскрыть строку подключения.
        const string connectionString =
            "Host=127.0.0.1;Port=1;Database=missing;Username=postgres;Password=postgres;" +
            "Timeout=1;Command Timeout=1";

        var services = new ServiceCollection();
        services.AddDbContext<GatewayDbContext>(options => options.UseNpgsql(connectionString));

        using var provider = services.BuildServiceProvider();
        var check = new DatabaseHealthCheck(provider.GetRequiredService<IServiceScopeFactory>());

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(result.Description).DoesNotContain("127.0.0.1");
        await Assert.That(result.Description).DoesNotContain("Password");
    }
}
