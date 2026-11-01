using System.Net;
using LtiIntegrationGateway.Contracts.Management.V1;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Contracts;
using LtiIntegrationGateway.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LtiIntegrationGateway.Tests.Features.Contracts;

public class ContractPublishAndActivationTests
{
    private static async Task CreateOutboundChannelAsync(HttpClient client, string name)
    {
        var response = await client.PostAsync($"/test/inbound-channels?name={Uri.EscapeDataString(name)}", content: null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    private static async Task SetRecipientQueueAsync(
        ManagementService.ManagementServiceClient client,
        string channelName,
        int version = 1,
        string queue = "dpv.inbox")
    {
        var update = await client.UpdateInboundContractVersionSettingsAsync(
            new UpdateContractVersionSettingsRequest
            {
                ChannelName = channelName,
                Version = version,
                Settings = new ContractVersionSettings { RecipientQueue = queue },
            },
            ManagementApi.Call());

        await Assert.That(update.Succeeded).IsTrue();
    }

    private static async Task PublishAndActivateAsync(
        ManagementService.ManagementServiceClient client,
        string channelName,
        string document,
        int version = 1)
    {
        await client.UploadInboundContractVersionAsync(
            new UploadContractRequest { ChannelName = channelName, Document = document },
            ManagementApi.Call());

        await SetRecipientQueueAsync(client, channelName, version);

        var publish = await client.PublishInboundContractVersionAsync(
            new PublishContractVersionRequest { ChannelName = channelName, Version = version },
            ManagementApi.Call());

        await Assert.That(publish.Succeeded).IsTrue();

        var activate = await client.ActivateInboundContractVersionAsync(
            new ActivateContractVersionRequest { ChannelName = channelName, Version = version },
            ManagementApi.Call());

        await Assert.That(activate.Succeeded).IsTrue();
    }

    [Test]
    public async Task Publish_extracts_operations_readable_via_api()
    {
        using var factory = ManagementHostFactory.WithStorage(ManagementApi.Settings());
        using var http = ManagementApi.CreateClient(factory);
        await CreateOutboundChannelAsync(http, "bank-a");

        using var OutboundChannel = ManagementApi.CreateChannel(factory);
        var grpc = new ManagementService.ManagementServiceClient(OutboundChannel);

        await grpc.UploadInboundContractVersionAsync(
            new UploadContractRequest
            {
                ChannelName = "bank-a",
                Document = OpenApiSamples.OpenApi30(("POST", "/callbacks/contracts", "receiveContract")),
            },
            ManagementApi.Call());

        await SetRecipientQueueAsync(grpc, "bank-a");

        var publish = await grpc.PublishInboundContractVersionAsync(
            new PublishContractVersionRequest { ChannelName = "bank-a", Version = 1 },
            ManagementApi.Call());

        await Assert.That(publish.Succeeded).IsTrue();

        var operations = await grpc.ListInboundContractOperationsAsync(
            new ListContractOperationsRequest { ChannelName = "bank-a", Version = 1 },
            ManagementApi.Call());

        await Assert.That(operations.Operations.Single().HttpMethod).IsEqualTo("POST");
        await Assert.That(operations.Operations.Single().Template).IsEqualTo("/callbacks/contracts");
        await Assert.That(operations.Operations.Single().OperationId).IsEqualTo("receiveContract");
    }

    [Test]
    public async Task Duplicate_operation_id_is_rejected_on_publish()
    {
        using var factory = ManagementHostFactory.WithStorage(ManagementApi.Settings());
        using var http = ManagementApi.CreateClient(factory);
        await CreateOutboundChannelAsync(http, "bank-a");

        using var OutboundChannel = ManagementApi.CreateChannel(factory);
        var grpc = new ManagementService.ManagementServiceClient(OutboundChannel);

        await grpc.UploadInboundContractVersionAsync(
            new UploadContractRequest
            {
                ChannelName = "bank-a",
                Document = OpenApiSamples.OpenApi30(
                    ("POST", "/callbacks/contracts", "duplicateId"),
                    ("GET", "/callbacks/orders", "duplicateId")),
            },
            ManagementApi.Call());

        await SetRecipientQueueAsync(grpc, "bank-a");

        var publish = await grpc.PublishInboundContractVersionAsync(
            new PublishContractVersionRequest { ChannelName = "bank-a", Version = 1 },
            ManagementApi.Call());

        await Assert.That(publish.Succeeded).IsFalse();
        await Assert.That(publish.Problems.Any(problem => problem.Code == "operation_id")).IsTrue();
    }

    [Test]
    public async Task Conflict_with_active_route_of_other_channel_is_rejected()
    {
        using var pair = new GatewayPair(ManagementApi.Settings());
        using var http = ManagementApi.CreateClient(pair.Management);
        await CreateOutboundChannelAsync(http, "bank-a");
        await CreateOutboundChannelAsync(http, "bank-b");

        using var OutboundChannel = ManagementApi.CreateChannel(pair.Management);
        var grpc = new ManagementService.ManagementServiceClient(OutboundChannel);

        await PublishAndActivateAsync(grpc, "bank-a", OpenApiSamples.OpenApi30WithPath("POST", "/callbacks/contracts"));
        await pair.ApplyAsync();

        await grpc.UploadInboundContractVersionAsync(
            new UploadContractRequest
            {
                ChannelName = "bank-b",
                Document = OpenApiSamples.OpenApi30WithPath("POST", "/callbacks/contracts"),
            },
            ManagementApi.Call());

        await SetRecipientQueueAsync(grpc, "bank-b");

        var publish = await grpc.PublishInboundContractVersionAsync(
            new PublishContractVersionRequest { ChannelName = "bank-b", Version = 1 },
            ManagementApi.Call());

        await Assert.That(publish.Succeeded).IsFalse();
        await Assert.That(publish.Problems.Any(problem => problem.Code == "conflict")).IsTrue();

        using var flow = pair.Flow.CreateClient();
        var stillServed = await flow.PostAsync("/callbacks/contracts", null);

        await Assert.That(stillServed.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
    }

    [Test]
    public async Task Activation_registers_routes_and_removes_previous_ones()
    {
        using var pair = new GatewayPair(ManagementApi.Settings());
        using var http = ManagementApi.CreateClient(pair.Management);
        await CreateOutboundChannelAsync(http, "bank-a");

        using var OutboundChannel = ManagementApi.CreateChannel(pair.Management);
        var grpc = new ManagementService.ManagementServiceClient(OutboundChannel);

        await PublishAndActivateAsync(grpc, "bank-a", OpenApiSamples.OpenApi30WithPath("POST", "/callbacks/contracts"));
        await pair.ApplyAsync();

        using var flow = pair.Flow.CreateClient();

        await Assert.That((await flow.PostAsync("/callbacks/contracts", null)).StatusCode)
            .IsEqualTo(HttpStatusCode.Accepted);

        await grpc.UploadInboundContractVersionAsync(
            new UploadContractRequest
            {
                ChannelName = "bank-a",
                Document = OpenApiSamples.OpenApi30WithPath("GET", "/callbacks/other"),
            },
            ManagementApi.Call());

        await SetRecipientQueueAsync(grpc, "bank-a", version: 2);

        await grpc.PublishInboundContractVersionAsync(
            new PublishContractVersionRequest { ChannelName = "bank-a", Version = 2 },
            ManagementApi.Call());

        var activated = await grpc.ActivateInboundContractVersionAsync(
            new ActivateContractVersionRequest { ChannelName = "bank-a", Version = 2 },
            ManagementApi.Call());

        await Assert.That(activated.Succeeded).IsTrue();
        await pair.ApplyAsync();

        await Assert.That((await flow.PostAsync("/callbacks/contracts", null)).StatusCode)
            .IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That((await flow.GetAsync("/callbacks/other")).StatusCode)
            .IsEqualTo(HttpStatusCode.Accepted);

        using var scope = pair.Management.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
        var active = await db.ContractVersions.SingleAsync(version => version.Status == ContractVersionStatus.Active);

        await Assert.That(active.Version).IsEqualTo(2);
    }

    [Test]
    public async Task Published_version_cannot_be_published_again()
    {
        using var factory = ManagementHostFactory.WithStorage(ManagementApi.Settings());
        using var http = ManagementApi.CreateClient(factory);
        await CreateOutboundChannelAsync(http, "bank-a");

        using var OutboundChannel = ManagementApi.CreateChannel(factory);
        var grpc = new ManagementService.ManagementServiceClient(OutboundChannel);

        await grpc.UploadInboundContractVersionAsync(
            new UploadContractRequest
            {
                ChannelName = "bank-a",
                Document = OpenApiSamples.OpenApi30WithPath("POST", "/callbacks/contracts"),
            },
            ManagementApi.Call());

        await SetRecipientQueueAsync(grpc, "bank-a");

        await grpc.PublishInboundContractVersionAsync(
            new PublishContractVersionRequest { ChannelName = "bank-a", Version = 1 },
            ManagementApi.Call());

        var second = await grpc.PublishInboundContractVersionAsync(
            new PublishContractVersionRequest { ChannelName = "bank-a", Version = 1 },
            ManagementApi.Call());

        await Assert.That(second.Succeeded).IsFalse();
        await Assert.That(second.Problems.Any(problem => problem.Code == "version")).IsTrue();
    }
}
