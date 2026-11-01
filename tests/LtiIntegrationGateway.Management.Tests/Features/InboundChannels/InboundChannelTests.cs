using System.Net;
using System.Text.Json;
using LtiIntegrationGateway.Features.Contracts.UploadContractVersion;
using LtiIntegrationGateway.Features.InboundChannels.CreateInboundChannel;
using LtiIntegrationGateway.Features.InboundChannels.UpdateInboundChannelSettings;
using LtiIntegrationGateway.Infrastructure.Management;
using LtiIntegrationGateway.Infrastructure.OpenApi;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Contracts;
using LtiIntegrationGateway.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace LtiIntegrationGateway.Tests.Features.InboundChannels;

/// <summary>Создание и настройка входящего канала, отделённого от outbound-канала.</summary>
public class InboundChannelTests
{
    private const string Name = "bank-a";

    [Test]
    public async Task Create_inbound_channel_defaults_to_async_and_creates_draft_version_one()
    {
        using var database = new PostgresTestDatabase();
        database.Migrate();
        using var db = database.CreateContext();
        var audit = new ManagementAuditWriter(db);
        var handler = new CreateInboundChannelHandler(db, new MicrosoftOpenApiDocumentReader());

        var result = await handler.HandleAsync(new CreateInboundChannelCommand(
            Name,
            OpenApiSamples.OpenApi30WithPath("POST", "/callbacks/contracts")));

        await Assert.That(result.Problems).IsEmpty();
        await Assert.That(result.ChannelId).IsNotNull();
        await Assert.That(result.ContractVersion).IsEqualTo(1);

        var channel = await db.InboundChannels.SingleAsync(entity => entity.Name == Name);
        var version = await db.ContractVersions.SingleAsync();

        await Assert.That(channel.ResponseMode).IsEqualTo(InboundResponseMode.Async);
        await Assert.That(channel.InboundResponseTimeoutMs).IsEqualTo(30_000);
        await Assert.That(version.Version).IsEqualTo(1);
        await Assert.That(version.Status).IsEqualTo(ContractVersionStatus.Draft);
        await Assert.That(version.InboundChannelId).IsEqualTo(channel.Id);
        await Assert.That(db.ChannelAuditEntries.Single().Direction).IsEqualTo(ChannelDirection.Inbound);
    }

    [Test]
    public async Task Same_name_is_allowed_in_inbound_and_outbound_directions()
    {
        using var database = new PostgresTestDatabase();
        database.Migrate();
        using var db = database.CreateContext();
        db.OutboundChannels.Add(new OutboundChannel { Id = Guid.NewGuid(), Name = Name, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var result = await new CreateInboundChannelHandler(
                db,
                new MicrosoftOpenApiDocumentReader())
            .HandleAsync(new CreateInboundChannelCommand(
                Name,
                OpenApiSamples.OpenApi30WithPath("POST", "/callbacks/contracts")));

        await Assert.That(result.Problems).IsEmpty();
        await Assert.That(await db.OutboundChannels.CountAsync()).IsEqualTo(1);
        await Assert.That(await db.InboundChannels.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task Update_settings_changes_mode_and_timeout_with_audit()
    {
        using var database = new PostgresTestDatabase();
        database.Migrate();
        using var db = database.CreateContext();
        var audit = new ManagementAuditWriter(db);
        var channel = await CreateAsync(db);

        var result = await new UpdateInboundChannelSettingsHandler(db)
            .HandleAsync(new UpdateInboundChannelSettingsCommand(
                Name,
                InboundResponseMode.Sync,
                12_345));

        await Assert.That(result.Problems).IsEmpty();
        await Assert.That(channel.ResponseMode).IsEqualTo(InboundResponseMode.Sync);
        await Assert.That(channel.InboundResponseTimeoutMs).IsEqualTo(12_345);
        await Assert.That(await db.ChannelAuditEntries.AnyAsync(
            entry => entry.Operation == "update-inbound-channel-settings" &&
                entry.Direction == ChannelDirection.Inbound)).IsTrue();
    }

    [Test]
    public async Task Upload_after_create_gets_next_contract_version()
    {
        using var database = new PostgresTestDatabase();
        database.Migrate();
        using var db = database.CreateContext();
        var audit = new ManagementAuditWriter(db);
        await CreateAsync(db);

        var result = await new UploadContractVersionHandler(db, new MicrosoftOpenApiDocumentReader(), audit)
            .HandleAsync(new UploadContractVersionCommand(
                Name,
                OpenApiSamples.OpenApi30WithPath("POST", "/callbacks/contracts")));

        await Assert.That(result.Problems).IsEmpty();
        await Assert.That(result.Version).IsEqualTo(2);
    }

    [Test]
    public async Task Contract_cannot_be_uploaded_to_outbound_channel()
    {
        using var database = new PostgresTestDatabase();
        database.Migrate();
        using var db = database.CreateContext();
        db.OutboundChannels.Add(new OutboundChannel { Id = Guid.NewGuid(), Name = Name, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var result = await new UploadContractVersionHandler(
                db,
                new MicrosoftOpenApiDocumentReader(),
                new ManagementAuditWriter(db))
            .HandleAsync(new UploadContractVersionCommand(
                Name,
                OpenApiSamples.OpenApi30WithPath("POST", "/callbacks/contracts")));

        await Assert.That(result.Problems).Contains("channel: канал не найден");
        await Assert.That(await db.ContractVersions.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task Product_route_accepts_outbound_channel_but_rejects_inbound_channel()
    {
        using var database = new PostgresTestDatabase();
        database.Migrate();
        using var db = database.CreateContext();
        var audit = new ManagementAuditWriter(db);
        await CreateAsync(db);

        var rejected = await new LtiIntegrationGateway.Features.ProductRoutes.RegisterProductRouteHandler(db)
            .HandleAsync(new LtiIntegrationGateway.Features.ProductRoutes.RegisterProductRouteCommand(
                "dpv",
                [Name],
                []));

        await Assert.That(rejected.Problems.Any(problem => problem.StartsWith("allowed_channels:", StringComparison.Ordinal)))
            .IsTrue();
    }

    private static async Task<InboundChannel> CreateAsync(
        LtiIntegrationGateway.Infrastructure.Persistence.GatewayDbContext db)
    {
        await new CreateInboundChannelHandler(db, new MicrosoftOpenApiDocumentReader())
            .HandleAsync(new CreateInboundChannelCommand(
                Name,
                OpenApiSamples.OpenApi30WithPath("POST", "/callbacks/contracts")));

        return await db.InboundChannels.SingleAsync(entity => entity.Name == Name);
    }
    [Test]
    public async Task Inbound_channel_and_settings_are_available_over_rest()
    {
        using var factory = ManagementHostFactory.WithStorage(ManagementApi.Settings());
        using var client = ManagementApi.CreateClient(factory);
        var document = OpenApiSamples.OpenApi30WithPath("POST", "/callbacks/contracts");

        var createdResponse = await client.PostAsync(
            "/v1/integrations/default/inbound-channels",
            ManagementApi.Json(JsonSerializer.Serialize(new { name = Name, document })));

        await Assert.That(createdResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using var createdJson = JsonDocument.Parse(await createdResponse.Content.ReadAsStringAsync());
        await Assert.That(createdJson.RootElement.GetProperty("contractVersion").GetInt32()).IsEqualTo(1);

        var listResponse = await client.GetAsync("/v1/integrations/default/inbound-channels");
        using var listJson = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        await Assert.That(listJson.RootElement.GetProperty("channels").GetArrayLength()).IsEqualTo(1);

        var readResponse = await client.GetAsync($"/v1/integrations/default/inbound-channels/{Name}");
        using var readJson = JsonDocument.Parse(await readResponse.Content.ReadAsStringAsync());
        await Assert.That(readJson.RootElement.GetProperty("channel").GetProperty("settings")
            .GetProperty("inboundResponseTimeoutMs").GetInt32()).IsEqualTo(30_000);

        var updatedResponse = await client.PatchAsync(
            $"/v1/integrations/default/inbound-channels/{Name}/settings",
            ManagementApi.Json("""
            {
              "responseMode": "INBOUND_RESPONSE_MODE_SYNC",
              "inboundResponseTimeoutMs": 1234
            }
            """));

        await Assert.That(updatedResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var afterResponse = await client.GetAsync($"/v1/integrations/default/inbound-channels/{Name}");
        using var afterJson = JsonDocument.Parse(await afterResponse.Content.ReadAsStringAsync());
        await Assert.That(afterJson.RootElement.GetProperty("channel").GetProperty("settings")
            .GetProperty("inboundResponseTimeoutMs").GetInt32()).IsEqualTo(1234);
    }

}
