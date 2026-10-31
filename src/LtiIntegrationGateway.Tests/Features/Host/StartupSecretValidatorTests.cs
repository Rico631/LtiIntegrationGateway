using LtiIntegrationGateway.Features.Host;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities;
using LtiIntegrationGateway.Tests.TestSupport;

namespace LtiIntegrationGateway.Tests.Features.Host;

/// <summary>Проверка секретов активной конфигурации при старте приложения.</summary>
public class StartupSecretValidatorTests
{
    [Test]
    public async Task Active_channel_without_secret_is_reported()
    {
        using var database = new SqliteInMemoryDatabase();
        database.Migrate();

        await AddChannelAsync(database, "bank-a", ChannelAuthType.Bearer, "Secrets:BankA:Token");

        using var context = database.CreateContext();
        var missing = await CreateValidator(context).FindMissingAsync();

        await Assert.That(missing.Count).IsEqualTo(1);
        await Assert.That(missing[0]).Contains("bank-a");
        await Assert.That(missing[0]).Contains("Secrets:BankA:Token");
    }

    [Test]
    public async Task Active_channel_with_secret_is_not_reported()
    {
        using var database = new SqliteInMemoryDatabase();
        database.Migrate();

        await AddChannelAsync(database, "bank-a", ChannelAuthType.ApiKey, "Secrets:BankA:Key");

        using var context = database.CreateContext();
        var secrets = new TestSecretStore().With("Secrets:BankA:Key", "value");
        using var certificates = new TestChannelCertificateStore();
        var missing = await new StartupSecretValidator(context, secrets, certificates).FindMissingAsync();

        await Assert.That(missing).IsEmpty();
    }

    [Test]
    public async Task Draft_channel_does_not_block_startup()
    {
        using var database = new SqliteInMemoryDatabase();
        database.Migrate();

        await AddChannelAsync(database, "bank-a", ChannelAuthType.Bearer, "Secrets:BankA:Token", ChannelRevisionStatus.Draft);

        using var context = database.CreateContext();
        var missing = await CreateValidator(context).FindMissingAsync();

        await Assert.That(missing).IsEmpty();
    }

    [Test]
    public async Task Active_channel_with_missing_client_certificate_is_reported()
    {
        using var database = new SqliteInMemoryDatabase();
        database.Migrate();

        await AddChannelAsync(
            database,
            "bank-a",
            ChannelAuthType.ClientCertificate,
            "Certificates:BankA");

        using var context = database.CreateContext();
        var missing = await CreateValidator(context).FindMissingAsync();

        await Assert.That(missing.Count).IsEqualTo(1);
        await Assert.That(missing[0]).Contains("Certificates:BankA");
    }

    [Test]
    public async Task Active_contract_version_without_secret_is_reported()
    {
        using var database = new SqliteInMemoryDatabase();
        database.Migrate();

        await using (var context = database.CreateContext())
        {
            var channel = new Channel { Id = Guid.NewGuid(), Name = "bank-a", CreatedAt = DateTimeOffset.UtcNow };

            context.Channels.Add(channel);
            context.ContractVersions.Add(new ContractVersion
            {
                Id = Guid.NewGuid(),
                ChannelId = channel.Id,
                Version = 1,
                SourceFormat = ContractSourceFormat.OpenApi30,
                Document = "{}",
                Status = ContractVersionStatus.Active,
                AuthType = ChannelAuthType.Bearer,
                ConfigKey = "Secrets:BankA:ContractToken",
                CreatedAt = DateTimeOffset.UtcNow,
            });

            await context.SaveChangesAsync();
        }

        using var verifyContext = database.CreateContext();
        var missing = await CreateValidator(verifyContext).FindMissingAsync();

        await Assert.That(missing.Count).IsEqualTo(1);
        await Assert.That(missing[0]).Contains("bank-a");
        await Assert.That(missing[0]).Contains("Secrets:BankA:ContractToken");
    }

    private static StartupSecretValidator CreateValidator(GatewayDbContext context) =>
        new(context, new TestSecretStore(), new TestChannelCertificateStore());

    private static async Task AddChannelAsync(
        SqliteInMemoryDatabase database,
        string name,
        ChannelAuthType authType,
        string configKey,
        ChannelRevisionStatus status = ChannelRevisionStatus.Active)
    {
        await using var context = database.CreateContext();

        var channel = new Channel { Id = Guid.NewGuid(), Name = name, CreatedAt = DateTimeOffset.UtcNow };

        context.Channels.Add(channel);
        context.ChannelRevisions.Add(new ChannelRevision
        {
            Id = Guid.NewGuid(),
            ChannelId = channel.Id,
            Number = 1,
            Status = status,
            BaseUrl = "https://bank-a.example",
            AuthType = authType,
            ConfigKey = configKey,
            PerAttemptTimeoutMs = 5000,
            OverallCapMs = 30000,
            RetryPolicy = RetryPolicy.Empty,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await context.SaveChangesAsync();
    }
}
