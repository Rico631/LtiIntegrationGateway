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
        using var database = new SqliteInMemoryDatabase();
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
        var path = Path.Combine(
            Path.GetTempPath(),
            $"lti-gateway-missing-{Guid.NewGuid():N}",
            "gateway.db");

        var services = new ServiceCollection();
        services.AddDbContext<GatewayDbContext>(options => options.UseSqlite($"Data Source={path}"));

        using var provider = services.BuildServiceProvider();
        var check = new DatabaseHealthCheck(provider.GetRequiredService<IServiceScopeFactory>());

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(result.Description).DoesNotContain(path);
    }
}
