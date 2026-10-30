using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using LtiIntegrationGateway.Contracts.Management.V1;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities;
using LtiIntegrationGateway.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace LtiIntegrationGateway.Tests.Features.Management;

public class ManagementApiTests
{
    private static string SettingsJson(
        string baseUrl = "https://bank-a.example",
        string[]? methods = null,
        string[]? paths = null) =>
        JsonSerializer.Serialize(new
        {
            baseUrl,
            authType = "none",
            perAttemptTimeoutMs = 5000,
            overallCapMs = 30000,
            retryPolicy = new[]
            {
                new { match = "transport_error", statusCodes = Array.Empty<int>(), delaysMs = new[] { 100 } },
            },
            allowedMethods = methods ?? ["GET"],
            allowedPaths = paths ?? ["/api/contracts"],
        });

    private static StringContent Body(string json) => ManagementApi.Json(json);

    private static StringContent CreateChannelBody(string name, string baseUrl = "https://bank-a.example") =>
        Body(JsonSerializer.Serialize(new
        {
            name,
            settings = JsonSerializer.Deserialize<JsonElement>(SettingsJson(baseUrl)),
        }));

    private static StringContent UpdateDraftBody(int revision, string baseUrl, string[] methods) =>
        Body(JsonSerializer.Serialize(new
        {
            revision,
            settings = JsonSerializer.Deserialize<JsonElement>(SettingsJson(baseUrl, methods)),
        }));

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.Clone();
    }

    [Test]
    public async Task Transcoded_rest_matches_grpc_list_channels()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var client = ManagementApi.CreateClient(factory);

        await client.PostAsync("/v1/channels", CreateChannelBody("bank-a"));
        await client.PostAsync("/v1/channels", CreateChannelBody("bank-b"));

        var restJson = await ReadJsonAsync(await client.GetAsync("/v1/channels"));
        var restNames = restJson.GetProperty("channels")
            .EnumerateArray()
            .Select(channel => channel.GetProperty("name").GetString() ?? string.Empty)
            .ToArray();

        using var channel = ManagementApi.CreateChannel(factory);
        var grpcClient = new ManagementService.ManagementServiceClient(channel);
        var grpcResponse = await grpcClient.ListChannelsAsync(new ListChannelsRequest(), ManagementApi.Call());
        var grpcNames = grpcResponse.Channels.Select(item => item.Name).ToArray();

        await Assert.That(restNames).IsEquivalentTo(grpcNames);
        await Assert.That(restNames).IsEquivalentTo(new[] { "bank-a", "bank-b" });
    }

    [Test]
    public async Task Channel_lifecycle_is_available_over_rest()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var client = ManagementApi.CreateClient(factory);

        var created = await ReadJsonAsync(await client.PostAsync("/v1/channels", CreateChannelBody("bank-a")));

        await Assert.That(created.GetProperty("succeeded").GetBoolean()).IsTrue();
        await Assert.That(created.GetProperty("channelId").GetString()).IsNotEmpty();
        await Assert.That(created.GetProperty("revision").GetInt32()).IsEqualTo(1);

        var updated = await ReadJsonAsync(await client.PatchAsync(
            "/v1/channels/bank-a/draft",
            UpdateDraftBody(1, "https://bank-a.example", ["GET", "POST"])));

        await Assert.That(updated.GetProperty("succeeded").GetBoolean()).IsTrue();

        var published = await ReadJsonAsync(await client.PostAsync("/v1/channels/bank-a/revisions/1/publish", null));

        await Assert.That(published.GetProperty("succeeded").GetBoolean()).IsTrue();

        var activated = await ReadJsonAsync(await client.PostAsync("/v1/channels/bank-a/revisions/1/activate", null));

        await Assert.That(activated.GetProperty("succeeded").GetBoolean()).IsTrue();

        var channelJson = await ReadJsonAsync(await client.GetAsync("/v1/channels/bank-a"));
        var methods = channelJson.GetProperty("channel").GetProperty("activeSettings")
            .GetProperty("allowedMethods")
            .EnumerateArray()
            .Select(item => item.GetString() ?? string.Empty)
            .ToArray();

        await Assert.That(methods).IsEquivalentTo(new[] { "GET", "POST" });

        await client.PatchAsync("/v1/channels/bank-a/draft", UpdateDraftBody(0, "https://bank-a2.example", ["GET"]));
        await client.PostAsync("/v1/channels/bank-a/revisions/2/publish", null);
        await client.PostAsync("/v1/channels/bank-a/revisions/2/activate", null);

        var rolledBack = await ReadJsonAsync(await client.PostAsync("/v1/channels/bank-a/rollback", null));

        await Assert.That(rolledBack.GetProperty("succeeded").GetBoolean()).IsTrue();

        var afterRollback = await ReadJsonAsync(await client.GetAsync("/v1/channels/bank-a"));

        await Assert.That(afterRollback.GetProperty("channel").GetProperty("revisions")
            .EnumerateArray()
            .Single(revision => revision.GetProperty("status").GetString() == "Active")
            .GetProperty("number").GetInt32()).IsEqualTo(1);
    }

    [Test]
    public async Task Invalid_publication_returns_all_problems_and_keeps_active_revision()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var client = ManagementApi.CreateClient(factory);

        await client.PostAsync("/v1/channels", CreateChannelBody("bank-a"));
        await client.PatchAsync("/v1/channels/bank-a/draft", UpdateDraftBody(1, "https://bank-a.example", ["GET"]));
        await client.PostAsync("/v1/channels/bank-a/revisions/1/publish", null);
        await client.PostAsync("/v1/channels/bank-a/revisions/1/activate", null);

        // Новый черновик с недопустимым URL и недопустимым шаблоном пути.
        await client.PatchAsync("/v1/channels/bank-a/draft", Body(JsonSerializer.Serialize(new
        {
            revision = 0,
            settings = JsonSerializer.Deserialize<JsonElement>(SettingsJson("http://insecure.example", ["GET"], ["callbacks"])),
        })));

        var publish = await ReadJsonAsync(await client.PostAsync("/v1/channels/bank-a/revisions/2/publish", null));

        await Assert.That(publish.GetProperty("succeeded").GetBoolean()).IsFalse();
        await Assert.That(publish.GetProperty("problems").GetArrayLength()).IsGreaterThanOrEqualTo(2);

        var channelJson = await ReadJsonAsync(await client.GetAsync("/v1/channels/bank-a"));
        var active = channelJson.GetProperty("channel").GetProperty("revisions")
            .EnumerateArray()
            .Single(revision => revision.GetProperty("status").GetString() == "Active");

        await Assert.That(active.GetProperty("number").GetInt32()).IsEqualTo(1);
    }

    [Test]
    public async Task Product_route_is_registered_and_read_over_rest()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var client = ManagementApi.CreateClient(factory);

        var registered = await ReadJsonAsync(await client.PutAsync(
            "/v1/products/dpv/route",
            Body(JsonSerializer.Serialize(new
            {
                productId = "dpv",
                allowedChannels = new[] { "bank-a" },
                allowedQueueAddresses = new[] { "dpv.inbox" },
            }))));

        await Assert.That(registered.GetProperty("succeeded").GetBoolean()).IsTrue();

        var route = await ReadJsonAsync(await client.GetAsync("/v1/products/dpv/route"));

        await Assert.That(route.GetProperty("allowedChannels").EnumerateArray()
            .Select(item => item.GetString() ?? string.Empty).ToArray()).IsEquivalentTo(new[] { "bank-a" });
        await Assert.That(route.GetProperty("allowedQueueAddresses").EnumerateArray()
            .Select(item => item.GetString() ?? string.Empty).ToArray()).IsEquivalentTo(new[] { "dpv.inbox" });
    }

    [Test]
    public async Task Unknown_product_route_returns_not_found()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var client = ManagementApi.CreateClient(factory);

        var response = await client.GetAsync("/v1/products/unknown/route");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Outbound_operation_status_is_readable_by_message_id()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var client = ManagementApi.CreateClient(factory);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();

            db.OutboundOperations.Add(new OutboundOperation
            {
                Id = Guid.NewGuid(),
                MessageId = "msg-status-1",
                ProductId = "dpv",
                ChannelName = "bank-a",
                Status = OutboundOperationStatus.Completed,
                Outcome = "HTTP_RESPONSE",
                HttpStatusCode = 201,
                AttemptCount = 2,
                CreatedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow,
            });

            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync("/v1/operations/msg-status-1");
        var body = await ReadJsonAsync(response);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(body.GetProperty("status").GetString()).IsEqualTo("Completed");
        await Assert.That(body.GetProperty("outcome").GetString()).IsEqualTo("HTTP_RESPONSE");
        await Assert.That(body.GetProperty("httpStatusCode").GetInt32()).IsEqualTo(201);
        await Assert.That(body.GetProperty("attemptCount").GetInt32()).IsEqualTo(2);
    }

    [Test]
    public async Task Unknown_outbound_operation_returns_not_found()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var client = ManagementApi.CreateClient(factory);

        var response = await client.GetAsync("/v1/operations/missing");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }
}