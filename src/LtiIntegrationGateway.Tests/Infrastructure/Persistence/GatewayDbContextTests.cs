using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Inbound;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Outbound;
using LtiIntegrationGateway.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace LtiIntegrationGateway.Tests.Infrastructure.Persistence;

public class GatewayDbContextTests
{
    [Test]
    public async Task Initial_migration_creates_direction_specific_channel_tables()
    {
        using var database = new SqliteInMemoryDatabase();
        using var context = database.CreateContext();

        await context.Database.MigrateAsync();

        var applied = await context.Database.GetAppliedMigrationsAsync();
        var outboundChannels = await database.CountAsync(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'OutboundChannels'");
        var inboundChannels = await database.CountAsync(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'InboundChannels'");
        var sharedChannels = await database.CountAsync(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Channels'");

        await Assert.That(applied.Any(migration => migration.EndsWith("_InitialSchema", StringComparison.Ordinal)))
            .IsTrue();
        await Assert.That(outboundChannels).IsEqualTo(1);
        await Assert.That(inboundChannels).IsEqualTo(1);
        await Assert.That(sharedChannels).IsEqualTo(0);
    }

    [Test]
    public async Task Unix_timestamp_columns_are_removed_and_time_order_is_preserved()
    {
        using var database = new SqliteInMemoryDatabase();
        database.Migrate();

        var older = new DateTimeOffset(2026, 10, 31, 10, 0, 0, TimeSpan.Zero);
        var newer = older.AddMinutes(1);

        await using (var context = database.CreateContext())
        {
            context.OutboundMessageLogs.AddRange(
                Outbound("out-new", newer),
                Outbound("out-old", older));
            context.InboundMessageLogs.AddRange(
                Inbound("in-new", newer),
                Inbound("in-old", older));

            await context.SaveChangesAsync();
        }

        var outboundColumns = await database.CountAsync(
            "SELECT COUNT(*) FROM pragma_table_info('OutboundMessageLogs') " +
            "WHERE name IN ('CreatedAtUnixMs')");
        var inboundColumns = await database.CountAsync(
            "SELECT COUNT(*) FROM pragma_table_info('InboundMessageLogs') " +
            "WHERE name IN ('ReceivedAtUnixMs')");
        var operationQuotaColumns = await database.CountAsync(
            "SELECT COUNT(*) FROM pragma_table_info('ContractOperations') " +
            "WHERE name LIKE 'RateLimit%'");

        await Assert.That(outboundColumns).IsEqualTo(0);
        await Assert.That(inboundColumns).IsEqualTo(0);
        await Assert.That(operationQuotaColumns).IsEqualTo(0);

        await using var verify = database.CreateContext();

        var outboundOrder = await verify.OutboundMessageLogs
            .OrderBy(log => log.CreatedAt)
            .Select(log => log.MessageId)
            .ToListAsync();
        var inboundOrder = await verify.InboundMessageLogs
            .OrderBy(log => log.ReceivedAt)
            .Select(log => log.MessageId)
            .ToListAsync();

        await Assert.That(outboundOrder).IsEquivalentTo(new[] { "out-old", "out-new" });
        await Assert.That(inboundOrder).IsEquivalentTo(new[] { "in-old", "in-new" });
    }

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

    private static OutboundMessageLog Outbound(string messageId, DateTimeOffset createdAt) => new()
    {
        Id = Guid.NewGuid(),
        MessageId = messageId,
        ProductId = "dpv",
        ChannelName = "bank-a",
        DeliveryAddress = "dpv.inbox",
        HttpMethod = "GET",
        RelativePath = "/api/contracts",
        DeliveryStatus = OutboundDeliveryStatus.Sent,
        CreatedAt = createdAt,
    };

    private static InboundMessageLog Inbound(string messageId, DateTimeOffset receivedAt) => new()
    {
        Id = Guid.NewGuid(),
        MessageId = messageId,
        ChannelName = "bank-a",
        HttpMethod = "POST",
        RelativePath = "/callbacks",
        DeliveryStatus = InboundDeliveryStatus.Sent,
        ReceivedAt = receivedAt,
    };
}
