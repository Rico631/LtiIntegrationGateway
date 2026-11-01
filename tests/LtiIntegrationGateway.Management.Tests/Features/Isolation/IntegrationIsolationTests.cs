using LtiIntegrationGateway.Contracts.Management.V1;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LtiIntegrationGateway.Tests.Features.Isolation;

/// <summary>
/// Изоляция интеграций: одинаковые имена, маршруты и продукты не пересекаются между интеграциями.
/// </summary>
public class IntegrationIsolationTests
{
    private const string First = "bank-a";
    private const string Second = "bank-b";

    private static ChannelSettings ChannelSettings(string baseUrl) => new()
    {
        BaseUrl = baseUrl,
        AuthType = "none",
        PerAttemptTimeoutMs = 5_000,
        OverallCapMs = 30_000,
        RetryPolicy = { new RetryRule { Match = "transport_error", DelaysMs = { 1_000 } } },
    };

    private static string InboundDocument() =>
        OpenApiSamples.OpenApi30(("POST", "/callbacks/contracts", "receiveContract"));

    [Test]
    public async Task Same_channel_name_in_two_integrations_creates_independent_channels()
    {
        using var factory = ManagementHostFactory.WithStorage(ManagementApi.Settings());
        using var http = ManagementApi.CreateClient(factory);
        using var channel = ManagementApi.CreateChannel(factory);
        var grpc = new ManagementService.ManagementServiceClient(channel);

        var first = await grpc.CreateOutboundChannelAsync(
            new CreateChannelRequest
            {
                Name = "payments",
                Integration = First,
                Settings = ChannelSettings("https://bank-a.example"),
            },
            ManagementApi.Call());

        var second = await grpc.CreateOutboundChannelAsync(
            new CreateChannelRequest
            {
                Name = "payments",
                Integration = Second,
                Settings = ChannelSettings("https://bank-b.example"),
            },
            ManagementApi.Call());

        await Assert.That(first.Succeeded).IsTrue();
        await Assert.That(second.Succeeded).IsTrue();
        await Assert.That(first.ChannelId).IsNotEqualTo(second.ChannelId);

        var listed = await grpc.ListOutboundChannelsAsync(
            new ListChannelsRequest { Integration = First },
            ManagementApi.Call());

        await Assert.That(listed.Channels.Count).IsEqualTo(1);
        await Assert.That(listed.Channels[0].ChannelId).IsEqualTo(first.ChannelId);

        var updated = await grpc.UpdateOutboundChannelDraftAsync(
            new UpdateChannelDraftRequest
            {
                ChannelName = "payments",
                Integration = First,
                Settings = ChannelSettings("https://bank-a.changed"),
            },
            ManagementApi.Call());

        await Assert.That(updated.Succeeded).IsTrue();

        var other = await grpc.GetOutboundChannelAsync(
            new GetChannelRequest { ChannelName = "payments", Integration = Second },
            ManagementApi.Call());

        await Assert.That(other.Channel.ChannelId).IsEqualTo(second.ChannelId);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();

        var baseUrls = await db.ChannelRevisions
            .Where(revision => revision.OutboundChannel!.Name == "payments")
            .Select(revision => new { revision.OutboundChannel!.IntegrationId, revision.BaseUrl })
            .ToListAsync();

        await Assert.That(baseUrls.Single(entry => entry.IntegrationId == First).BaseUrl)
            .IsEqualTo("https://bank-a.changed");
        await Assert.That(baseUrls.Single(entry => entry.IntegrationId == Second).BaseUrl)
            .IsEqualTo("https://bank-b.example");
    }

    [Test]
    public async Task Same_route_active_in_another_integration_does_not_block_publication()
    {
        using var factory = ManagementHostFactory.WithStorage(ManagementApi.Settings());
        using var channel = ManagementApi.CreateChannel(factory);
        var grpc = new ManagementService.ManagementServiceClient(channel);

        foreach (var integration in new[] { First, Second })
        {
            var created = await grpc.CreateInboundChannelAsync(
                new CreateInboundChannelRequest
                {
                    Name = "callbacks",
                    Integration = integration,
                    Document = InboundDocument(),
                },
                ManagementApi.Call());

            await Assert.That(created.Succeeded).IsTrue();

            var settings = await grpc.UpdateInboundContractVersionSettingsAsync(
                new UpdateContractVersionSettingsRequest
                {
                    ChannelName = "callbacks",
                    Version = 1,
                    Integration = integration,
                    Settings = new ContractVersionSettings { RecipientQueue = "dpv.inbox" },
                },
                ManagementApi.Call());

            await Assert.That(settings.Succeeded).IsTrue();

            var published = await grpc.PublishInboundContractVersionAsync(
                new PublishContractVersionRequest
                {
                    ChannelName = "callbacks",
                    Version = 1,
                    Integration = integration,
                },
                ManagementApi.Call());

            await Assert.That(published.Succeeded).IsTrue();

            var activated = await grpc.ActivateInboundContractVersionAsync(
                new ActivateContractVersionRequest
                {
                    ChannelName = "callbacks",
                    Version = 1,
                    Integration = integration,
                },
                ManagementApi.Call());

            await Assert.That(activated.Succeeded).IsTrue();
        }
    }

    [Test]
    public async Task Product_route_does_not_accept_channel_of_another_integration()
    {
        using var factory = ManagementHostFactory.WithStorage(ManagementApi.Settings());
        using var http = ManagementApi.CreateClient(factory);
        using var channel = ManagementApi.CreateChannel(factory);
        var grpc = new ManagementService.ManagementServiceClient(channel);

        await grpc.CreateOutboundChannelAsync(
            new CreateChannelRequest
            {
                Name = "payments",
                Integration = First,
                Settings = ChannelSettings("https://bank-a.example"),
            },
            ManagementApi.Call());

        var foreign = await grpc.RegisterProductRouteAsync(
            new RegisterProductRouteRequest
            {
                ProductId = "dpv",
                Integration = Second,
                AllowedChannels = { "payments" },
            },
            ManagementApi.Call());

        await Assert.That(foreign.Succeeded).IsFalse();
        await Assert.That(foreign.Problems.Any(problem => problem.Code == "allowed_channels")).IsTrue();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();

        await Assert.That(await db.ProductRoutes
            .AnyAsync(route => route.IntegrationId == Second && route.ProductId == "dpv")).IsFalse();
    }
}
