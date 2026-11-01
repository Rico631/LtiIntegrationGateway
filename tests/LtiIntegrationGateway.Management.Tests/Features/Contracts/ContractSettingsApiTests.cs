using System.Net;
using Grpc.Core;
using Grpc.Net.Client;
using LtiIntegrationGateway.Contracts.Management.V1;
using LtiIntegrationGateway.Tests.TestSupport;

namespace LtiIntegrationGateway.Tests.Features.Contracts;

public class ContractSettingsApiTests
{
    private static async Task CreateOutboundChannelAsync(HttpClient client, string name)
    {
        var response = await client.PostAsync($"/test/inbound-channels?name={Uri.EscapeDataString(name)}", content: null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    private static async Task<ManagementService.ManagementServiceClient> PrepareDraftAsync(
        ManagementHostFactory factory,
        GrpcChannel OutboundChannel)
    {
        using var http = ManagementApi.CreateClient(factory);
        await CreateOutboundChannelAsync(http, "bank-a");

        var grpc = new ManagementService.ManagementServiceClient(OutboundChannel);

        await grpc.UploadInboundContractVersionAsync(
            new UploadContractRequest
            {
                ChannelName = "bank-a",
                Document = OpenApiSamples.OpenApi30WithPath("POST", "/callbacks/contracts"),
            },
            ManagementApi.Call());

        return grpc;
    }

    [Test]
    public async Task Draft_settings_are_saved_and_read_back()
    {
        using var factory = ManagementHostFactory.WithStorage(ManagementApi.Settings());
        using var OutboundChannel = ManagementApi.CreateChannel(factory);
        var grpc = await PrepareDraftAsync(factory, OutboundChannel);

        var update = await grpc.UpdateInboundContractVersionSettingsAsync(
            new UpdateContractVersionSettingsRequest
            {
                ChannelName = "bank-a",
                Version = 1,
                Settings = new ContractVersionSettings
                {
                    RecipientQueue = "dpv.inbox",
                    AuthType = "api_key",
                    ConfigKey = "bank-a.inbound",
                    HeaderName = "X-Bank-Key",
                    RateLimitPermit = 25,
                    RateLimitWindowSeconds = 60,
                },
            },
            ManagementApi.Call());

        await Assert.That(update.Succeeded).IsTrue();

        var read = await grpc.GetInboundContractVersionAsync(
            new GetContractVersionRequest { ChannelName = "bank-a", Version = 1 },
            ManagementApi.Call());

        await Assert.That(read.Status).IsEqualTo("Draft");
        await Assert.That(read.Settings.RecipientQueue).IsEqualTo("dpv.inbox");
        await Assert.That(read.Settings.AuthType).IsEqualTo("api_key");
        await Assert.That(read.Settings.ConfigKey).IsEqualTo("bank-a.inbound");
        await Assert.That(read.Settings.HeaderName).IsEqualTo("X-Bank-Key");
        await Assert.That(read.Settings.RateLimitPermit).IsEqualTo(25);
        await Assert.That(read.Settings.RateLimitWindowSeconds).IsEqualTo(60);

        // Операции извлекаются при публикации, поэтому у черновика их ещё нет.
        await Assert.That(read.Operations.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Published_version_settings_are_immutable()
    {
        using var factory = ManagementHostFactory.WithStorage(ManagementApi.Settings());
        using var OutboundChannel = ManagementApi.CreateChannel(factory);
        var grpc = await PrepareDraftAsync(factory, OutboundChannel);

        await grpc.UpdateInboundContractVersionSettingsAsync(
            new UpdateContractVersionSettingsRequest
            {
                ChannelName = "bank-a",
                Version = 1,
                Settings = new ContractVersionSettings { RecipientQueue = "dpv.inbox" },
            },
            ManagementApi.Call());

        var publish = await grpc.PublishInboundContractVersionAsync(
            new PublishContractVersionRequest { ChannelName = "bank-a", Version = 1 },
            ManagementApi.Call());

        await Assert.That(publish.Succeeded).IsTrue();

        var update = await grpc.UpdateInboundContractVersionSettingsAsync(
            new UpdateContractVersionSettingsRequest
            {
                ChannelName = "bank-a",
                Version = 1,
                Settings = new ContractVersionSettings { RecipientQueue = "other.inbox" },
            },
            ManagementApi.Call());

        await Assert.That(update.Succeeded).IsFalse();
        await Assert.That(update.Problems.Any(problem => problem.Code == "version")).IsTrue();

        var read = await grpc.GetInboundContractVersionAsync(
            new GetContractVersionRequest { ChannelName = "bank-a", Version = 1 },
            ManagementApi.Call());

        await Assert.That(read.Settings.RecipientQueue).IsEqualTo("dpv.inbox");
        await Assert.That(read.Status).IsEqualTo("Published");
        await Assert.That(read.Operations.Count).IsEqualTo(1);
        await Assert.That(read.Operations[0].Template).IsEqualTo("/callbacks/contracts");
    }

    [Test]
    public async Task Unsupported_auth_type_is_rejected()
    {
        using var factory = ManagementHostFactory.WithStorage(ManagementApi.Settings());
        using var OutboundChannel = ManagementApi.CreateChannel(factory);
        var grpc = await PrepareDraftAsync(factory, OutboundChannel);

        var bearer = await grpc.UpdateInboundContractVersionSettingsAsync(
            new UpdateContractVersionSettingsRequest
            {
                ChannelName = "bank-a",
                Version = 1,
                Settings = new ContractVersionSettings { AuthType = "bearer" },
            },
            ManagementApi.Call());

        var unknown = await grpc.UpdateInboundContractVersionSettingsAsync(
            new UpdateContractVersionSettingsRequest
            {
                ChannelName = "bank-a",
                Version = 1,
                Settings = new ContractVersionSettings { AuthType = "magic" },
            },
            ManagementApi.Call());

        await Assert.That(bearer.Problems.Any(problem => problem.Code == "auth_type")).IsTrue();
        await Assert.That(unknown.Problems.Any(problem => problem.Code == "auth_type")).IsTrue();
    }

    [Test]
    public async Task Inconsistent_secret_settings_are_rejected()
    {
        using var factory = ManagementHostFactory.WithStorage(ManagementApi.Settings());
        using var OutboundChannel = ManagementApi.CreateChannel(factory);
        var grpc = await PrepareDraftAsync(factory, OutboundChannel);

        var withoutConfigKey = await grpc.UpdateInboundContractVersionSettingsAsync(
            new UpdateContractVersionSettingsRequest
            {
                ChannelName = "bank-a",
                Version = 1,
                Settings = new ContractVersionSettings { AuthType = "api_key" },
            },
            ManagementApi.Call());

        var basicWithoutUser = await grpc.UpdateInboundContractVersionSettingsAsync(
            new UpdateContractVersionSettingsRequest
            {
                ChannelName = "bank-a",
                Version = 1,
                Settings = new ContractVersionSettings
                {
                    AuthType = "basic",
                    ConfigKey = "bank-a.inbound",
                },
            },
            ManagementApi.Call());

        var partialQuota = await grpc.UpdateInboundContractVersionSettingsAsync(
            new UpdateContractVersionSettingsRequest
            {
                ChannelName = "bank-a",
                Version = 1,
                Settings = new ContractVersionSettings { RateLimitPermit = 10 },
            },
            ManagementApi.Call());

        await Assert.That(withoutConfigKey.Problems.Any(problem => problem.Code == "config_key")).IsTrue();
        await Assert.That(basicWithoutUser.Problems.Any(problem => problem.Code == "user_name")).IsTrue();
        await Assert.That(partialQuota.Problems.Any(problem => problem.Code == "rate_limit")).IsTrue();
    }

    [Test]
    public async Task Unknown_version_returns_not_found()
    {
        using var factory = ManagementHostFactory.WithStorage(ManagementApi.Settings());
        using var OutboundChannel = ManagementApi.CreateChannel(factory);
        var grpc = await PrepareDraftAsync(factory, OutboundChannel);

        RpcException? failure = null;

        try
        {
            await grpc.GetInboundContractVersionAsync(
                new GetContractVersionRequest { ChannelName = "bank-a", Version = 99 },
                ManagementApi.Call());
        }
        catch (RpcException exception)
        {
            failure = exception;
        }

        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.StatusCode).IsEqualTo(StatusCode.NotFound);
    }

    [Test]
    public async Task Publish_without_recipient_queue_is_rejected()
    {
        using var factory = ManagementHostFactory.WithStorage(ManagementApi.Settings());
        using var OutboundChannel = ManagementApi.CreateChannel(factory);
        var grpc = await PrepareDraftAsync(factory, OutboundChannel);

        var publish = await grpc.PublishInboundContractVersionAsync(
            new PublishContractVersionRequest { ChannelName = "bank-a", Version = 1 },
            ManagementApi.Call());

        await Assert.That(publish.Succeeded).IsFalse();
        await Assert.That(publish.Problems.Any(problem => problem.Code == "queue")).IsTrue();
    }
}
