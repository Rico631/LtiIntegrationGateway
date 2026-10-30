using LtiIntegrationGateway.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace LtiIntegrationGateway.Tests.Infrastructure.Persistence;

public class GatewayDbContextTests
{
    [Test]
    public async Task In_memory_database_survives_between_consecutive_operations()
    {
        using var database = new SqliteInMemoryDatabase();

        using (var first = database.CreateContext())
        {
            await first.Database.ExecuteSqlRawAsync("CREATE TABLE probe (id INTEGER PRIMARY KEY)");
        }

        using (var second = database.CreateContext())
        {
            var tableCount = await database.CountAsync(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'probe'");

            await Assert.That(tableCount).IsEqualTo(1);
            await Assert.That(await second.Database.CanConnectAsync()).IsTrue();
        }
    }
}
