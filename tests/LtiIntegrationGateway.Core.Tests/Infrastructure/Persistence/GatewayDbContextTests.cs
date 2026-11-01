using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Contracts;
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
        using var database = new PostgresTestDatabase();
        using var context = database.CreateContext();

        await context.Database.MigrateAsync();

        var applied = await context.Database.GetAppliedMigrationsAsync();
        var outboundChannels = await database.CountAsync(
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_name = 'OutboundChannels'");
        var inboundChannels = await database.CountAsync(
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_name = 'InboundChannels'");
        var sharedChannels = await database.CountAsync(
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_name = 'Channels'");

        await Assert.That(applied.Any(migration => migration.EndsWith("_InitialSchema", StringComparison.Ordinal)))
            .IsTrue();
        await Assert.That(outboundChannels).IsEqualTo(1);
        await Assert.That(inboundChannels).IsEqualTo(1);
        await Assert.That(sharedChannels).IsEqualTo(0);
    }

    [Test]
    public async Task Unix_timestamp_columns_are_removed_and_time_order_is_preserved()
    {
        using var database = new PostgresTestDatabase();
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
            "SELECT COUNT(*) FROM information_schema.columns " +
            "WHERE table_name = 'OutboundMessageLogs' AND column_name = 'CreatedAtUnixMs'");
        var inboundColumns = await database.CountAsync(
            "SELECT COUNT(*) FROM information_schema.columns " +
            "WHERE table_name = 'InboundMessageLogs' AND column_name = 'ReceivedAtUnixMs'");
        var operationQuotaColumns = await database.CountAsync(
            "SELECT COUNT(*) FROM information_schema.columns " +
            "WHERE table_name = 'ContractOperations' AND column_name LIKE 'RateLimit%'");

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
    public async Task Database_survives_between_consecutive_operations()
    {
        using var database = new PostgresTestDatabase();

        using (var first = database.CreateContext())
        {
            await first.Database.ExecuteSqlRawAsync("CREATE TABLE probe (id integer primary key)");
        }

        using (var second = database.CreateContext())
        {
            var tableCount = await database.CountAsync(
                "SELECT COUNT(*) FROM information_schema.tables WHERE table_name = 'probe'");

            await Assert.That(tableCount).IsEqualTo(1);
            await Assert.That(await second.Database.CanConnectAsync()).IsTrue();
        }
    }

    [Test]
    public async Task Second_active_revision_of_channel_is_rejected()
    {
        using var database = new PostgresTestDatabase();
        database.Migrate();

        var channelId = Guid.NewGuid();

        await using (var context = database.CreateContext())
        {
            context.OutboundChannels.Add(new OutboundChannel
            {
                Id = channelId,
                Name = "bank-a",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            context.ChannelRevisions.Add(ActiveRevision(channelId, 1));

            await context.SaveChangesAsync();
        }

        await using var second = database.CreateContext();
        second.ChannelRevisions.Add(ActiveRevision(channelId, 2));

        await Assert.ThrowsAsync<DbUpdateException>(async () => await second.SaveChangesAsync());
    }

    [Test]
    public async Task Second_active_version_of_contract_is_rejected()
    {
        using var database = new PostgresTestDatabase();
        database.Migrate();

        var channelId = Guid.NewGuid();

        await using (var context = database.CreateContext())
        {
            context.InboundChannels.Add(new InboundChannel
            {
                Id = channelId,
                Name = "callbacks",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            context.ContractVersions.Add(ActiveVersion(channelId, 1));

            await context.SaveChangesAsync();
        }

        await using var second = database.CreateContext();
        second.ContractVersions.Add(ActiveVersion(channelId, 2));

        await Assert.ThrowsAsync<DbUpdateException>(async () => await second.SaveChangesAsync());
    }

    private static ChannelRevision ActiveRevision(Guid channelId, int number) => new()
    {
        Id = Guid.NewGuid(),
        OutboundChannelId = channelId,
        Number = number,
        Status = ChannelRevisionStatus.Active,
        BaseUrl = "https://bank-a.example",
        AuthType = ChannelAuthType.None,
        PerAttemptTimeoutMs = 5000,
        OverallCapMs = 30000,
        RetryPolicy = RetryPolicy.Empty,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static ContractVersion ActiveVersion(Guid channelId, int version) => new()
    {
        Id = Guid.NewGuid(),
        InboundChannelId = channelId,
        Version = version,
        SourceFormat = ContractSourceFormat.OpenApi30,
        Document = "{}",
        Status = ContractVersionStatus.Active,
        AuthType = ChannelAuthType.None,
        CreatedAt = DateTimeOffset.UtcNow,
    };

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
