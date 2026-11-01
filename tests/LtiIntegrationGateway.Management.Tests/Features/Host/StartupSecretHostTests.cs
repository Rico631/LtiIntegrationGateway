using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;
using LtiIntegrationGateway.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace LtiIntegrationGateway.Tests.Features.Host;

/// <summary>Fail-fast при старте хоста: активная конфигурация без секрета не должна запускаться.</summary>
public class StartupSecretHostTests
{
    [Test]
    public async Task Host_start_fails_when_active_channel_secret_is_missing()
    {
        using var database = new PostgresTestDatabase();
        await SeedActiveChannelAsync(database);

        using var factory = ManagementHostFactory.WithExistingStorage(database.ConnectionString);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await Task.Yield();
            _ = factory.Services;
        });

        await Assert.That(exception!.Message).Contains("Secrets:BankA:Token");
    }

    [Test]
    public async Task Host_starts_when_active_channel_secret_is_configured()
    {
        using var database = new PostgresTestDatabase();
        await SeedActiveChannelAsync(database);

        using var factory = ManagementHostFactory.WithExistingStorage(
            database.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Secrets:BankA:Token"] = "configured",
            });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        await Assert.That(response.IsSuccessStatusCode).IsTrue();
    }

    private static async Task SeedActiveChannelAsync(PostgresTestDatabase database)
    {
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();

        var OutboundChannel = new OutboundChannel { Id = Guid.NewGuid(), Name = "bank-a", CreatedAt = DateTimeOffset.UtcNow };

        context.OutboundChannels.Add(OutboundChannel);
        context.ChannelRevisions.Add(new ChannelRevision
        {
            Id = Guid.NewGuid(),
            OutboundChannelId = OutboundChannel.Id,
            Number = 1,
            Status = ChannelRevisionStatus.Active,
            BaseUrl = "https://bank-a.example",
            AuthType = ChannelAuthType.Bearer,
            ConfigKey = "Secrets:BankA:Token",
            PerAttemptTimeoutMs = 5000,
            OverallCapMs = 30000,
            RetryPolicy = RetryPolicy.Empty,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await context.SaveChangesAsync();
    }
}
