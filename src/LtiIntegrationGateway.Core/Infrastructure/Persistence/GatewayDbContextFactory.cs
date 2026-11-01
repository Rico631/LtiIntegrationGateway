using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LtiIntegrationGateway.Infrastructure.Persistence;

/// <summary>
/// Design-time фабрика контекста для <c>dotnet ef</c>: приложение при этом не запускается,
/// поэтому строка подключения берётся из окружения или подставляется файловая заглушка.
/// </summary>
public sealed class GatewayDbContextFactory : IDesignTimeDbContextFactory<GatewayDbContext>
{
    private const string DefaultConnectionString =
        "Host=localhost;Database=gateway_design;Username=postgres;Password=postgres";

    public GatewayDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Gateway") ?? DefaultConnectionString;

        var options = new DbContextOptionsBuilder<GatewayDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new GatewayDbContext(options);
    }
}
