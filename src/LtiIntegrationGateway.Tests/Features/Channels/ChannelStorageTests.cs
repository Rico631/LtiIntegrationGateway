using LtiIntegrationGateway.Features.Channels.CreateChannel;
using LtiIntegrationGateway.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace LtiIntegrationGateway.Tests.Features.Channels;

public class ChannelStorageTests
{
    [Test]
    public async Task Configuration_is_persisted_without_secret_values()
    {
        using var database = new SqliteInMemoryDatabase();
        database.Migrate();

        using (var db = database.CreateContext())
        {
            await new CreateChannelHandler(db).HandleAsync(
                new CreateChannelCommand(ChannelTestData.ChannelName, ChannelTestData.ValidConfiguration()));
        }

        using (var db = database.CreateContext())
        {
            var revision = await db.ChannelRevisions.SingleAsync();

            await Assert.That(revision.ConfigKey).IsEqualTo("Secrets:BankA:Token");
            await Assert.That(revision.RetryPolicy.Rules.Count).IsEqualTo(1);
            await Assert.That(revision.RetryPolicy.Rules[0].DelaysMs.SequenceEqual([1000, 2000, 4000])).IsTrue();
        }
    }

    [Test]
    public async Task Model_has_no_properties_for_secret_values()
    {
        using var database = new SqliteInMemoryDatabase();
        database.Migrate();

        using var db = database.CreateContext();

        var suspicious = db.Model
            .GetEntityTypes()
            .SelectMany(entity => entity.GetProperties())
            .Select(property => property.Name)
            .Where(name =>
                name.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("token", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("password", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        await Assert.That(string.Join(", ", suspicious)).IsEqualTo(string.Empty);
    }
}
