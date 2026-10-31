using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Grpc.Core;
using LtiIntegrationGateway.Contracts.Management.V1;
using LtiIntegrationGateway.Features.Channels;
using LtiIntegrationGateway.Infrastructure.Management;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities;
using LtiIntegrationGateway.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace LtiIntegrationGateway.Tests.Features.Management;

public class ChannelHealthApiTests
{
    private const string ChannelName = "bank-a";
    private const string ConfigKey = "Channels:bank-a:Token";
    private const string CertificateKey = "Channels:bank-a:Certificate";
    private const string SecretValue = "super-secret-token-value";
    private const string BodyMarker = "MESSAGE-BODY-MARKER";

    /// <summary>Размер журнала ошибок в тесте «последние ошибки»: больше окна выборки среза.</summary>
    private const int BacklogSize = 70;

    private static readonly DateTimeOffset ErrorOrigin = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private static Dictionary<string, string?> Settings(params (string Key, string? Value)[] extra)
    {
        var settings = ManagementApi.Settings();

        foreach (var (key, value) in extra)
        {
            settings[key] = value;
        }

        return settings;
    }

    private static async Task SeedChannelAsync(
        GatewayWebApplicationFactory factory,
        ChannelAuthType authType = ChannelAuthType.None,
        string? configKey = null,
        ChannelRevisionStatus status = ChannelRevisionStatus.Active)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();

        var channel = new Channel
        {
            Id = Guid.NewGuid(),
            Name = ChannelName,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        channel.Revisions.Add(new ChannelRevision
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
            CreatedAt = DateTimeOffset.UtcNow,
        });

        db.Channels.Add(channel);
        await db.SaveChangesAsync();
    }

    private static async Task AddOutboundErrorsAsync(
        GatewayWebApplicationFactory factory,
        int count,
        string channelName = ChannelName)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();

        for (var index = 0; index < count; index++)
        {
            var createdAt = ErrorOrigin.AddMinutes(-index);

            db.OutboundMessageLogs.Add(new OutboundMessageLog
            {
                Id = Guid.NewGuid(),
                MessageId = $"msg-{Guid.NewGuid():N}",
                ProductId = "dpv",
                ChannelName = channelName,
                DeliveryAddress = "product.inbox",
                HttpMethod = "GET",
                RelativePath = "/api/contracts",
                RequestBody = Encoding.UTF8.GetBytes(BodyMarker),
                ResponseBody = Encoding.UTF8.GetBytes(BodyMarker),
                HttpStatusCode = 502,
                Outcome = "HTTP_RESPONSE",
                ErrorCode = $"OUT-{index}",
                AttemptCount = 1,
                DeliveryStatus = OutboundDeliveryStatus.Sent,
                CreatedAt = createdAt,
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task AddInboundErrorsAsync(
        GatewayWebApplicationFactory factory,
        int count,
        string channelName = ChannelName)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();

        for (var index = 0; index < count; index++)
        {
            var receivedAt = ErrorOrigin.AddSeconds(-90 - index);

            db.InboundMessageLogs.Add(new InboundMessageLog
            {
                Id = Guid.NewGuid(),
                MessageId = $"msg-{Guid.NewGuid():N}",
                ChannelName = channelName,
                HttpMethod = "POST",
                RelativePath = "/callbacks",
                RequestBody = Encoding.UTF8.GetBytes(BodyMarker),
                DeliveryStatus = InboundDeliveryStatus.Failed,
                ErrorCode = $"IN-{index}",
                ReceivedAt = receivedAt,
            });
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Наполняет оба журнала ошибками больше окна выборки: последние по времени записи вставляются
    /// последними, поэтому выборка без SQL-сортировки вернула бы старые ошибки.
    /// </summary>
    private static async Task AddErrorBacklogAsync(GatewayWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();

        for (var index = 0; index < BacklogSize; index++)
        {
            var createdAt = ErrorOrigin.AddSeconds(index);

            db.OutboundMessageLogs.Add(new OutboundMessageLog
            {
                Id = Guid.NewGuid(),
                MessageId = $"msg-out-{index}",
                ProductId = "dpv",
                ChannelName = ChannelName,
                DeliveryAddress = "product.inbox",
                HttpMethod = "GET",
                RelativePath = "/api/contracts",
                HttpStatusCode = 500,
                Outcome = "HTTP_RESPONSE",
                ErrorCode = $"OUT-{index}",
                AttemptCount = 1,
                DeliveryStatus = OutboundDeliveryStatus.Sent,
                CreatedAt = createdAt,
            });

            var receivedAt = createdAt.AddMilliseconds(500);

            db.InboundMessageLogs.Add(new InboundMessageLog
            {
                Id = Guid.NewGuid(),
                MessageId = $"msg-in-{index}",
                ChannelName = ChannelName,
                HttpMethod = "POST",
                RelativePath = "/callbacks",
                DeliveryStatus = InboundDeliveryStatus.Failed,
                ErrorCode = $"IN-{index}",
                ReceivedAt = receivedAt,
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task<(HttpStatusCode Status, string Text, JsonElement Body)> GetHealthAsync(
        HttpClient client,
        string channelName = ChannelName)
    {
        var response = await client.GetAsync($"/v1/channels/{channelName}/health");
        var text = await response.Content.ReadAsStringAsync();

        using var document = JsonDocument.Parse(text);

        return (response.StatusCode, text, document.RootElement.Clone());
    }

    [Test]
    public async Task Health_reports_active_revision_and_configured_secret()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(Settings((ConfigKey, SecretValue)));
        using var client = ManagementApi.CreateClient(factory);

        await SeedChannelAsync(factory, ChannelAuthType.Bearer, ConfigKey);

        var (status, text, body) = await GetHealthAsync(client);

        await Assert.That(status).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(body.GetProperty("channelName").GetString()).IsEqualTo(ChannelName);
        await Assert.That(body.GetProperty("state").GetString()).IsEqualTo("ACTIVE");
        await Assert.That(body.GetProperty("activeRevision").GetInt32()).IsEqualTo(1);
        await Assert.That(body.GetProperty("secretConfigured").GetBoolean()).IsTrue();
        await Assert.That(body.GetProperty("secretConfigKey").GetString()).IsEqualTo(ConfigKey);
        await Assert.That(body.GetProperty("checkedAt").GetString()).IsNotNull();
        await Assert.That(text.Contains(SecretValue, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task Missing_secret_is_reported_with_key_and_without_value()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(
            Settings((ConfigKey, null), ("Secrets:Other:Token", SecretValue)));
        using var client = ManagementApi.CreateClient(factory);

        await SeedChannelAsync(factory, ChannelAuthType.Bearer, ConfigKey);

        var (status, text, body) = await GetHealthAsync(client);

        await Assert.That(status).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(body.GetProperty("secretConfigured").GetBoolean()).IsFalse();
        await Assert.That(body.GetProperty("secretConfigKey").GetString()).IsEqualTo(ConfigKey);
        await Assert.That(text.Contains(SecretValue, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task Channel_without_active_revision_returns_state_and_errors()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(Settings());
        using var client = ManagementApi.CreateClient(factory);

        await SeedChannelAsync(factory, ChannelAuthType.Bearer, ConfigKey, ChannelRevisionStatus.Draft);
        await AddInboundErrorsAsync(factory, 1);

        var (status, _, body) = await GetHealthAsync(client);

        await Assert.That(status).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(body.GetProperty("state").GetString()).IsEqualTo("NO_ACTIVE_REVISION");
        await Assert.That(body.GetProperty("activeRevision").GetInt32()).IsEqualTo(0);
        await Assert.That(body.GetProperty("secretConfigured").GetBoolean()).IsFalse();
        await Assert.That(body.GetProperty("errors").GetArrayLength()).IsEqualTo(1);
    }

    [Test]
    public async Task Unknown_channel_returns_not_found_over_rest_and_grpc()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(Settings());
        using var client = ManagementApi.CreateClient(factory);

        var (status, _, _) = await GetHealthAsync(client, "missing");

        await Assert.That(status).IsEqualTo(HttpStatusCode.NotFound);

        using var channel = ManagementApi.CreateChannel(factory);
        var grpcClient = new ManagementService.ManagementServiceClient(channel);

        var exception = await Assert.ThrowsAsync<RpcException>(async () =>
            await grpcClient.GetChannelHealthAsync(
                new GetChannelHealthRequest { ChannelName = "missing" },
                ManagementApi.Call()));

        await Assert.That(exception!.StatusCode).IsEqualTo(StatusCode.NotFound);
    }

    [Test]
    public async Task Recent_errors_are_limited_and_hide_payloads()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(Settings());
        using var client = ManagementApi.CreateClient(factory);

        await SeedChannelAsync(factory);
        await AddOutboundErrorsAsync(factory, 7);
        await AddInboundErrorsAsync(factory, 2);

        var (status, text, body) = await GetHealthAsync(client);
        var errors = body.GetProperty("errors").EnumerateArray().ToArray();

        await Assert.That(status).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(errors.Count(entry => entry.GetProperty("direction").GetString() == "outbound"))
            .IsEqualTo(5);
        await Assert.That(errors.Count(entry => entry.GetProperty("direction").GetString() == "inbound"))
            .IsEqualTo(2);
        await Assert.That(errors[0].GetProperty("errorCode").GetString()).IsEqualTo("OUT-0");
        await Assert.That(errors[0].GetProperty("httpStatus").GetInt32()).IsEqualTo(502);
        await Assert.That(errors[0].GetProperty("outcome").GetString()).IsEqualTo("HTTP_RESPONSE");
        await Assert.That(errors[0].GetProperty("occurredAt").GetString()).IsNotNull();
        await Assert.That(errors
            .Where(entry => entry.GetProperty("direction").GetString() == "inbound")
            .All(entry => entry.GetProperty("httpStatus").GetInt32() == 0 &&
                          entry.GetProperty("outcome").GetString() == string.Empty)).IsTrue();
        await Assert.That(text.Contains(BodyMarker, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task Client_certificate_expiry_is_returned()
    {
        var notAfter = new DateTimeOffset(2035, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var certificate = TestCertificates.CreateSelfSigned(notAfter: notAfter);
        var material = Convert.ToBase64String(TestCertificates.ExportPfx(certificate, TestCertificates.Password));

        using var factory = GatewayWebApplicationFactory.WithStorage(Settings(
            (CertificateKey, material),
            (CertificateKey + ":Password", TestCertificates.Password)));
        using var client = ManagementApi.CreateClient(factory);

        await SeedChannelAsync(factory, ChannelAuthType.ClientCertificate, CertificateKey);

        var (status, _, body) = await GetHealthAsync(client);
        var expiresAt = DateTimeOffset.Parse(
            body.GetProperty("certificateExpiresAt").GetString()!,
            CultureInfo.InvariantCulture);

        await Assert.That(status).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(body.GetProperty("secretConfigured").GetBoolean()).IsTrue();
        await Assert.That(Math.Abs((expiresAt - notAfter).TotalSeconds)).IsLessThan(2);
    }

    [Test]
    public async Task Health_returns_latest_errors_beyond_the_window()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(Settings());
        using var client = ManagementApi.CreateClient(factory);

        await SeedChannelAsync(factory);
        await AddErrorBacklogAsync(factory);

        var (status, _, body) = await GetHealthAsync(client);
        var errors = body.GetProperty("errors").EnumerateArray().ToArray();

        var outbound = errors
            .Where(entry => entry.GetProperty("direction").GetString() == "outbound")
            .Select(entry => entry.GetProperty("errorCode").GetString())
            .ToArray();
        var inbound = errors
            .Where(entry => entry.GetProperty("direction").GetString() == "inbound")
            .Select(entry => entry.GetProperty("errorCode").GetString())
            .ToArray();

        var latest = Enumerable.Range(BacklogSize - 5, 5).Reverse().ToArray();

        await Assert.That(status).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(outbound.Length).IsEqualTo(5);
        await Assert.That(inbound.Length).IsEqualTo(5);
        await Assert.That(string.Join(",", outbound))
            .IsEqualTo(string.Join(",", latest.Select(index => $"OUT-{index}")));
        await Assert.That(string.Join(",", inbound))
            .IsEqualTo(string.Join(",", latest.Select(index => $"IN-{index}")));
    }

    [Test]
    public async Task Expired_client_certificate_keeps_expiry_in_response()
    {
        var notBefore = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var notAfter = new DateTimeOffset(2021, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var certificate = TestCertificates.CreateSelfSigned(notBefore: notBefore, notAfter: notAfter);
        var material = Convert.ToBase64String(TestCertificates.ExportPfx(certificate, TestCertificates.Password));

        using var factory = GatewayWebApplicationFactory.WithStorage(Settings(
            (CertificateKey, material),
            (CertificateKey + ":Password", TestCertificates.Password)));
        using var client = ManagementApi.CreateClient(factory);

        await SeedChannelAsync(factory, ChannelAuthType.ClientCertificate, CertificateKey);

        var (status, _, body) = await GetHealthAsync(client);
        var expiresAt = DateTimeOffset.Parse(
            body.GetProperty("certificateExpiresAt").GetString()!,
            CultureInfo.InvariantCulture);

        await Assert.That(status).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(body.GetProperty("secretConfigured").GetBoolean()).IsFalse();
        await Assert.That(Math.Abs((expiresAt - notAfter).TotalSeconds)).IsLessThan(2);
        await Assert.That(factory.Logging.Entries.Any(entry =>
            entry.Category == typeof(GetChannelHealthHandler).FullName &&
            entry.Message.Contains("срок действия сертификата истёк", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Health_read_is_audited_without_secret_values()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(Settings((ConfigKey, SecretValue)));
        using var client = ManagementApi.CreateClient(factory);

        await SeedChannelAsync(factory, ChannelAuthType.Bearer, ConfigKey);

        await GetHealthAsync(client);
        await GetHealthAsync(client, "missing");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();

        var entries = db.ManagementAuditEntries.ToArray();

        var succeeded = entries.Single(entry =>
            entry.Operation == "GetChannelHealth" && entry.Result == ManagementAuditResults.Succeeded);
        var rejected = entries.Single(entry =>
            entry.Operation == "GetChannelHealth" && entry.Result == ManagementAuditResults.Rejected);

        await Assert.That(succeeded.Target).IsEqualTo(ChannelName);
        await Assert.That(rejected.Target).IsEqualTo("missing");
        await Assert.That(entries.Any(entry =>
            (entry.Details ?? string.Empty).Contains(SecretValue, StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task Rest_and_grpc_health_responses_match()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(Settings((ConfigKey, SecretValue)));
        using var client = ManagementApi.CreateClient(factory);

        await SeedChannelAsync(factory, ChannelAuthType.Bearer, ConfigKey);
        await AddOutboundErrorsAsync(factory, 2);

        var (_, _, body) = await GetHealthAsync(client);

        using var channel = ManagementApi.CreateChannel(factory);
        var grpcClient = new ManagementService.ManagementServiceClient(channel);
        var grpcResponse = await grpcClient.GetChannelHealthAsync(
            new GetChannelHealthRequest { ChannelName = ChannelName },
            ManagementApi.Call());

        await Assert.That(grpcResponse.ChannelName).IsEqualTo(body.GetProperty("channelName").GetString());
        await Assert.That(grpcResponse.State).IsEqualTo(body.GetProperty("state").GetString());
        await Assert.That(grpcResponse.ActiveRevision).IsEqualTo(body.GetProperty("activeRevision").GetInt32());
        await Assert.That(grpcResponse.SecretConfigured)
            .IsEqualTo(body.GetProperty("secretConfigured").GetBoolean());
        await Assert.That(grpcResponse.SecretConfigKey)
            .IsEqualTo(body.GetProperty("secretConfigKey").GetString());
        await Assert.That(grpcResponse.Errors.Count).IsEqualTo(body.GetProperty("errors").GetArrayLength());
        await Assert.That(grpcResponse.Errors[0].ErrorCode)
            .IsEqualTo(body.GetProperty("errors")[0].GetProperty("errorCode").GetString());
    }
}