using LtiIntegrationGateway.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LtiIntegrationGateway.Infrastructure.Persistence;

/// <summary>
/// Design-time фабрика контекста для <c>dotnet ef</c>: приложение при этом не запускается,
/// поэтому строка подключения берётся из окружения или подставляется файловая заглушка.
/// </summary>
public sealed class GatewayDbContextFactory : IDesignTimeDbContextFactory<GatewayDbContext>
{
    private const string DefaultConnectionString = "Data Source=gateway.design.db";

    public GatewayDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Gateway") ?? DefaultConnectionString;

        var options = new DbContextOptionsBuilder<GatewayDbContext>()
            .UseSqlite(connectionString)
            .Options;

        return new GatewayDbContext(options);
    }
}
