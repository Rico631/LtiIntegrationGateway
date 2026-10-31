using LtiIntegrationGateway.Infrastructure.Persistence.Entities;
using LtiIntegrationGateway.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LtiIntegrationGateway.Tests.Infrastructure.Persistence;

public class GatewayDbContextTests
{
    [Test]
    public async Task Existing_timestamps_survive_removal_of_unix_columns()
    {
        using var database = new SqliteInMemoryDatabase();
        using var context = database.CreateContext();

        var migrator = context.Database.GetService<IMigrator>();

        await migrator.MigrateAsync("20261031101548_AddOutboundErrorIndex");

        var timestamp = new DateTimeOffset(2026, 10, 31, 10, 0, 0, TimeSpan.Zero);
        var unixMilliseconds = timestamp.ToUnixTimeMilliseconds();

        await context.Database.ExecuteSqlRawAsync(
            "INSERT INTO OutboundMessageLogs " +
            "(Id, MessageId, ProductId, ChannelName, DeliveryAddress, HttpMethod, RelativePath, " +
            "RequestBody, ResponseBody, AttemptCount, DeliveryStatus, CreatedAt, CreatedAtUnixMs) " +
            "VALUES ({0}, 'old-out', 'dpv', 'bank-a', 'dpv.inbox', 'GET', '/api', X'', X'', 0, " +
            "'Sent', '2026-10-31 10:00:00+00:00', {1})",
            Guid.NewGuid().ToString(),
            unixMilliseconds);
        await context.Database.ExecuteSqlRawAsync(
            "INSERT INTO InboundMessageLogs " +
            "(Id, MessageId, ChannelName, HttpMethod, RelativePath, RequestBody, DeliveryStatus, " +
            "ReceivedAt, ReceivedAtUnixMs) " +
            "VALUES ({0}, 'old-in', 'bank-a', 'POST', '/callbacks', X'', 'Sent', " +
            "'2026-10-31 10:00:00+00:00', {1})",
            Guid.NewGuid().ToString(),
            unixMilliseconds);

        await context.Database.MigrateAsync();

        await using var verify = database.CreateContext();
        var outbound = await verify.OutboundMessageLogs.SingleAsync();
        var inbound = await verify.InboundMessageLogs.SingleAsync();

        await Assert.That(outbound.CreatedAt.ToUnixTimeMilliseconds()).IsEqualTo(unixMilliseconds);
        await Assert.That(inbound.ReceivedAt.ToUnixTimeMilliseconds()).IsEqualTo(unixMilliseconds);
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
