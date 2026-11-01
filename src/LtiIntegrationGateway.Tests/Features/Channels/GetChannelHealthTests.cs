using LtiIntegrationGateway.Features.Channels.GetChannelHealth;
using LtiIntegrationGateway.Infrastructure.Management;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Inbound;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Outbound;
using LtiIntegrationGateway.Infrastructure.Security;
using LtiIntegrationGateway.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LtiIntegrationGateway.Tests.Features.Channels;

public class GetChannelHealthTests
{
    private const string ChannelName = "bank-a";
    private const string ConfigKey = "Channels:bank-a:Token";
    private const string CertificateKey = "Channels:bank-a:Certificate";

    private static readonly DateTimeOffset FixedNow = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private static GetChannelHealthHandler CreateHandler(
        GatewayDbContext db,
        ISecretStore secrets,
        IChannelCertificateStore certificates,
        TimeProvider? timeProvider = null) =>
        new(
            db,
            secrets,
            certificates,
            new ManagementAuditWriter(db),
            timeProvider ?? new ManualTimeProvider(),
            NullLogger<GetChannelHealthHandler>.Instance);

    private static OutboundChannel CreateChannel(
        ChannelAuthType authType = ChannelAuthType.None,
        string? configKey = null,
        ChannelRevisionStatus status = ChannelRevisionStatus.Active,
        int number = 1)
    {
        var OutboundChannel = new OutboundChannel { Id = Guid.NewGuid(), Name = ChannelName, CreatedAt = FixedNow };

        OutboundChannel.Revisions.Add(new ChannelRevision
        {
            Id = Guid.NewGuid(),
            OutboundChannelId = OutboundChannel.Id,
            Number = number,
            Status = status,
            BaseUrl = "https://bank-a.example",
            AuthType = authType,
            ConfigKey = configKey,
            PerAttemptTimeoutMs = 5000,
            OverallCapMs = 30000,
            CreatedAt = FixedNow,
        });

        return OutboundChannel;
    }

    private static OutboundMessageLog OutboundError(
        DateTimeOffset createdAt,
        string errorCode = "HTTP_STATUS",
        int httpStatus = 502,
        string outcome = "HTTP_RESPONSE") => new()
        {
            Id = Guid.NewGuid(),
            MessageId = Guid.NewGuid().ToString("N"),
            ProductId = "dpv",
            ChannelName = ChannelName,
            DeliveryAddress = "product.inbox",
            HttpMethod = "GET",
            RelativePath = "/api/contracts",
            RequestBody = "OUTBOUND-REQUEST-BODY"u8.ToArray(),
            ResponseBody = "OUTBOUND-RESPONSE-BODY"u8.ToArray(),
            HttpStatusCode = httpStatus,
            Outcome = outcome,
            ErrorCode = errorCode,
            AttemptCount = 1,
            DeliveryStatus = OutboundDeliveryStatus.Sent,
            CreatedAt = createdAt,
        };

    private static InboundMessageLog InboundError(DateTimeOffset receivedAt, string errorCode = "INVALID_JSON") => new()
    {
        Id = Guid.NewGuid(),
        MessageId = Guid.NewGuid().ToString("N"),
        ChannelName = ChannelName,
        HttpMethod = "POST",
        RelativePath = "/callbacks",
        RequestBody = "INBOUND-REQUEST-BODY"u8.ToArray(),
        DeliveryStatus = InboundDeliveryStatus.Failed,
        ErrorCode = errorCode,
        ReceivedAt = receivedAt,
    };

    [Test]
    public async Task Unknown_channel_returns_null_and_rejected_audit()
    {
        using var database = new SqliteInMemoryDatabase();
        database.Migrate();

        using var db = database.CreateContext();
        using var certificates = new TestChannelCertificateStore();

        var result = await CreateHandler(db, new TestSecretStore(), certificates)
            .HandleAsync(new GetChannelHealthQuery("missing"));

        await Assert.That(result.Health).IsNull();
        await Assert.That(result.Problems.Count).IsEqualTo(1);
        await Assert.That(await db.ManagementAuditEntries.CountAsync(entry =>
            entry.Operation == GetChannelHealthHandler.AuditOperation &&
            entry.Target == "missing" &&
            entry.Result == ManagementAuditResults.Rejected)).IsEqualTo(1);
    }

    [Test]
    public async Task Channel_without_active_revision_reports_state_and_reads_errors()
    {
        using var database = new SqliteInMemoryDatabase();
        database.Migrate();

        using var db = database.CreateContext();
        db.OutboundChannels.Add(CreateChannel(status: ChannelRevisionStatus.Draft));
        db.OutboundMessageLogs.Add(OutboundError(FixedNow));
        await db.SaveChangesAsync();

        using var certificates = new TestChannelCertificateStore();
        var result = await CreateHandler(db, new TestSecretStore(), certificates)
            .HandleAsync(new GetChannelHealthQuery(ChannelName));

        await Assert.That(result.Health).IsNotNull();
        await Assert.That(result.Health!.State).IsEqualTo(GetChannelHealthHandler.NoActiveRevisionState);
        await Assert.That(result.Health.ActiveRevision).IsEqualTo(0);
        await Assert.That(result.Health.SecretConfigured).IsFalse();
        await Assert.That(result.Health.SecretConfigKey).IsEqualTo(string.Empty);
        await Assert.That(result.Health.CertificateExpiresAt).IsNull();
        await Assert.That(result.Health.Errors.Count).IsEqualTo(1);
    }

    [Test]
    public async Task None_authorization_does_not_require_secret()
    {
        using var database = new SqliteInMemoryDatabase();
        database.Migrate();

        using var db = database.CreateContext();
        db.OutboundChannels.Add(CreateChannel(authType: ChannelAuthType.None));
        await db.SaveChangesAsync();

        using var certificates = new TestChannelCertificateStore();
        var result = await CreateHandler(db, new TestSecretStore(), certificates)
            .HandleAsync(new GetChannelHealthQuery(ChannelName));

        await Assert.That(result.Health!.State).IsEqualTo(GetChannelHealthHandler.ActiveState);
        await Assert.That(result.Health.ActiveRevision).IsEqualTo(1);
        await Assert.That(result.Health.SecretConfigured).IsTrue();
        await Assert.That(result.Health.SecretConfigKey).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Missing_secret_is_reported_with_configured_key()
    {
        using var database = new SqliteInMemoryDatabase();
        database.Migrate();

        using var db = database.CreateContext();
        db.OutboundChannels.Add(CreateChannel(ChannelAuthType.Bearer, ConfigKey));
        await db.SaveChangesAsync();

        using var certificates = new TestChannelCertificateStore();
        var result = await CreateHandler(db, new TestSecretStore(), certificates)
            .HandleAsync(new GetChannelHealthQuery(ChannelName));

        await Assert.That(result.Health!.SecretConfigured).IsFalse();
        await Assert.That(result.Health.SecretConfigKey).IsEqualTo(ConfigKey);
    }

    [Test]
    public async Task Configured_secret_is_reported_as_present()
    {
        using var database = new SqliteInMemoryDatabase();
        database.Migrate();

        using var db = database.CreateContext();
        db.OutboundChannels.Add(CreateChannel(ChannelAuthType.ApiKey, ConfigKey));
        await db.SaveChangesAsync();

        using var certificates = new TestChannelCertificateStore();
        var secrets = new TestSecretStore().With(ConfigKey, "secret-value-never-returned");
        var result = await CreateHandler(db, secrets, certificates)
            .HandleAsync(new GetChannelHealthQuery(ChannelName));

        await Assert.That(result.Health!.SecretConfigured).IsTrue();
        await Assert.That(result.Health.SecretConfigKey).IsEqualTo(ConfigKey);
    }

    [Test]
    public async Task Empty_config_key_with_secret_type_is_not_configured()
    {
        using var database = new SqliteInMemoryDatabase();
        database.Migrate();

        using var db = database.CreateContext();
        db.OutboundChannels.Add(CreateChannel(ChannelAuthType.Basic, configKey: null));
        await db.SaveChangesAsync();

        using var certificates = new TestChannelCertificateStore();
        var result = await CreateHandler(db, new TestSecretStore(), certificates)
            .HandleAsync(new GetChannelHealthQuery(ChannelName));

        await Assert.That(result.Health!.SecretConfigured).IsFalse();
        await Assert.That(result.Health.SecretConfigKey).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Client_certificate_expiry_comes_from_loaded_certificate()
    {
        using var database = new SqliteInMemoryDatabase();
        database.Migrate();

        var notAfter = new DateTimeOffset(2035, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var certificate = TestCertificates.CreateSelfSigned(notAfter: notAfter);
        var material = Convert.ToBase64String(TestCertificates.ExportPfx(certificate, TestCertificates.Password));

        using var certificates = new TestChannelCertificateStore()
            .With(CertificateKey, material, TestCertificates.Password);

        using var db = database.CreateContext();
        db.OutboundChannels.Add(CreateChannel(ChannelAuthType.ClientCertificate, CertificateKey));
        await db.SaveChangesAsync();

        var result = await CreateHandler(db, new TestSecretStore(), certificates)
            .HandleAsync(new GetChannelHealthQuery(ChannelName));

        await Assert.That(result.Health!.SecretConfigured).IsTrue();
        await Assert.That(result.Health.SecretConfigKey).IsEqualTo(CertificateKey);
        await Assert.That(result.Health.CertificateExpiresAt).IsNotNull();
        await Assert.That(Math.Abs(
            (result.Health.CertificateExpiresAt!.Value -
             new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero)).TotalSeconds)).IsLessThan(1);
    }

    [Test]
    public async Task Expired_client_certificate_keeps_expiry_and_is_not_configured()
    {
        using var database = new SqliteInMemoryDatabase();
        database.Migrate();

        var notBefore = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var notAfter = new DateTimeOffset(2021, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var certificate = TestCertificates.CreateSelfSigned(notBefore: notBefore, notAfter: notAfter);
        var material = Convert.ToBase64String(TestCertificates.ExportPfx(certificate, TestCertificates.Password));

        using var certificates = new TestChannelCertificateStore()
            .With(CertificateKey, material, TestCertificates.Password);

        using var db = database.CreateContext();
        db.OutboundChannels.Add(CreateChannel(ChannelAuthType.ClientCertificate, CertificateKey));
        await db.SaveChangesAsync();

        var result = await CreateHandler(db, new TestSecretStore(), certificates)
            .HandleAsync(new GetChannelHealthQuery(ChannelName));

        await Assert.That(result.Health!.SecretConfigured).IsFalse();
        await Assert.That(result.Health.CertificateExpiresAt).IsNotNull();
        await Assert.That(Math.Abs(
            (result.Health.CertificateExpiresAt!.Value - notAfter).TotalSeconds)).IsLessThan(1);
    }

    [Test]
    public async Task Not_yet_valid_client_certificate_keeps_expiry_and_is_not_configured()
    {
        using var database = new SqliteInMemoryDatabase();
        database.Migrate();

        var notBefore = DateTimeOffset.UtcNow.AddDays(10);
        var notAfter = DateTimeOffset.UtcNow.AddDays(400);
        using var certificate = TestCertificates.CreateSelfSigned(notBefore: notBefore, notAfter: notAfter);
        var material = Convert.ToBase64String(TestCertificates.ExportPfx(certificate, TestCertificates.Password));

        using var certificates = new TestChannelCertificateStore()
            .With(CertificateKey, material, TestCertificates.Password);

        using var db = database.CreateContext();
        db.OutboundChannels.Add(CreateChannel(ChannelAuthType.ClientCertificate, CertificateKey));
        await db.SaveChangesAsync();

        var result = await CreateHandler(db, new TestSecretStore(), certificates)
            .HandleAsync(new GetChannelHealthQuery(ChannelName));

        await Assert.That(result.Health!.SecretConfigured).IsFalse();
        await Assert.That(result.Health.CertificateExpiresAt).IsNotNull();
        await Assert.That(Math.Abs(
            (result.Health.CertificateExpiresAt!.Value -
             new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero)).TotalSeconds)).IsLessThan(1);
    }

    [Test]
    public async Task Recent_errors_are_limited_and_hide_payloads()
    {
        using var database = new SqliteInMemoryDatabase();
        database.Migrate();

        using var db = database.CreateContext();
        db.OutboundChannels.Add(CreateChannel());

        for (var index = 0; index < 7; index++)
        {
            db.OutboundMessageLogs.Add(OutboundError(FixedNow.AddMinutes(-index), $"OUT-{index}"));
        }

        db.InboundMessageLogs.Add(InboundError(FixedNow.AddSeconds(-90), "IN-0"));
        db.InboundMessageLogs.Add(InboundError(FixedNow.AddSeconds(-150), "IN-1"));

        // Успешный вызов без кода ошибки в выборку не попадает.
        var successful = OutboundError(FixedNow, "HTTP_STATUS");
        successful.ErrorCode = null;
        db.OutboundMessageLogs.Add(successful);

        await db.SaveChangesAsync();

        using var certificates = new TestChannelCertificateStore();
        var result = await CreateHandler(db, new TestSecretStore(), certificates)
            .HandleAsync(new GetChannelHealthQuery(ChannelName));

        var errors = result.Health!.Errors;

        await Assert.That(errors.Count).IsEqualTo(GetChannelHealthHandler.MaxErrorsPerDirection);
        await Assert.That(errors.All(entry => entry.Direction == GetChannelHealthHandler.OutboundDirection))
            .IsTrue();
        await Assert.That(string.Join(",", errors.Select(entry => entry.ErrorCode)))
            .IsEqualTo("OUT-0,OUT-1,OUT-2,OUT-3,OUT-4");
    }
}
