using System.Net;
using System.Text.Json;
using LtiIntegrationGateway.Contracts.Management.V1;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Contracts;
using LtiIntegrationGateway.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LtiIntegrationGateway.Tests.Features.Contracts;

public class ContractPublishAndActivationTests
{
    private static async Task CreateChannelAsync(HttpClient client, string name)
    {
        var response = await client.PostAsync("/v1/channels", ManagementApi.Json(JsonSerializer.Serialize(new
        {
            name,
            settings = new
            {
                baseUrl = "https://" + name + ".example",
                authType = "none",
                perAttemptTimeoutMs = 5000,
                overallCapMs = 30000,
                retryPolicy = new[]
                {
                    new { match = "transport_error", statusCodes = Array.Empty<int>(), delaysMs = new[] { 100 } },
                },
            },
        })));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    private static async Task SetRecipientQueueAsync(
        ManagementService.ManagementServiceClient client,
        string channelName,
        int version = 1,
        string queue = "dpv.inbox")
    {
        var update = await client.UpdateContractVersionSettingsAsync(
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
        await client.UploadContractAsync(
            new UploadContractRequest { ChannelName = channelName, Document = document },
            ManagementApi.Call());

        await SetRecipientQueueAsync(client, channelName, version);

        var publish = await client.PublishContractVersionAsync(
            new PublishContractVersionRequest { ChannelName = channelName, Version = version },
            ManagementApi.Call());

        await Assert.That(publish.Succeeded).IsTrue();

        var activate = await client.ActivateContractVersionAsync(
            new ActivateContractVersionRequest { ChannelName = channelName, Version = version },
            ManagementApi.Call());

        await Assert.That(activate.Succeeded).IsTrue();
    }

    [Test]
    public async Task Publish_extracts_operations_readable_via_api()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var http = ManagementApi.CreateClient(factory);
        await CreateChannelAsync(http, "bank-a");

        using var channel = ManagementApi.CreateChannel(factory);
        var grpc = new ManagementService.ManagementServiceClient(channel);

        await grpc.UploadContractAsync(
            new UploadContractRequest
            {
                ChannelName = "bank-a",
                Document = OpenApiSamples.OpenApi30(("POST", "/callbacks/contracts", "receiveContract")),
            },
            ManagementApi.Call());

        await SetRecipientQueueAsync(grpc, "bank-a");

        var publish = await grpc.PublishContractVersionAsync(
            new PublishContractVersionRequest { ChannelName = "bank-a", Version = 1 },
            ManagementApi.Call());

        await Assert.That(publish.Succeeded).IsTrue();

        var operations = await grpc.ListContractOperationsAsync(
            new ListContractOperationsRequest { ChannelName = "bank-a", Version = 1 },
            ManagementApi.Call());

        await Assert.That(operations.Operations.Single().HttpMethod).IsEqualTo("POST");
        await Assert.That(operations.Operations.Single().Template).IsEqualTo("/callbacks/contracts");
        await Assert.That(operations.Operations.Single().OperationId).IsEqualTo("receiveContract");
    }

    [Test]
    public async Task Duplicate_operation_id_is_rejected_on_publish()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var http = ManagementApi.CreateClient(factory);
        await CreateChannelAsync(http, "bank-a");

        using var channel = ManagementApi.CreateChannel(factory);
        var grpc = new ManagementService.ManagementServiceClient(channel);

        await grpc.UploadContractAsync(
            new UploadContractRequest
            {
                ChannelName = "bank-a",
                Document = OpenApiSamples.OpenApi30(
                    ("POST", "/callbacks/contracts", "duplicateId"),
                    ("GET", "/callbacks/orders", "duplicateId")),
            },
            ManagementApi.Call());

        await SetRecipientQueueAsync(grpc, "bank-a");

        var publish = await grpc.PublishContractVersionAsync(
            new PublishContractVersionRequest { ChannelName = "bank-a", Version = 1 },
            ManagementApi.Call());

        await Assert.That(publish.Succeeded).IsFalse();
        await Assert.That(publish.Problems.Any(problem => problem.Code == "operation_id")).IsTrue();
    }

    [Test]
    public async Task Conflict_with_active_route_of_other_channel_is_rejected()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var http = ManagementApi.CreateClient(factory);
        await CreateChannelAsync(http, "bank-a");
        await CreateChannelAsync(http, "bank-b");

        using var channel = ManagementApi.CreateChannel(factory);
        var grpc = new ManagementService.ManagementServiceClient(channel);

        await PublishAndActivateAsync(grpc, "bank-a", OpenApiSamples.OpenApi30WithPath("POST", "/callbacks/contracts"));

        await grpc.UploadContractAsync(
            new UploadContractRequest
            {
                ChannelName = "bank-b",
                Document = OpenApiSamples.OpenApi30WithPath("POST", "/callbacks/contracts"),
            },
            ManagementApi.Call());

        await SetRecipientQueueAsync(grpc, "bank-b");

        var publish = await grpc.PublishContractVersionAsync(
            new PublishContractVersionRequest { ChannelName = "bank-b", Version = 1 },
            ManagementApi.Call());

        await Assert.That(publish.Succeeded).IsFalse();
        await Assert.That(publish.Problems.Any(problem => problem.Code == "conflict")).IsTrue();

        var stillServed = await http.PostAsync("/callbacks/contracts", null);

        await Assert.That(stillServed.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
    }

    [Test]
    public async Task Activation_registers_routes_and_removes_previous_ones()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var http = ManagementApi.CreateClient(factory);
        await CreateChannelAsync(http, "bank-a");

        using var channel = ManagementApi.CreateChannel(factory);
        var grpc = new ManagementService.ManagementServiceClient(channel);

        await PublishAndActivateAsync(grpc, "bank-a", OpenApiSamples.OpenApi30WithPath("POST", "/callbacks/contracts"));

        await Assert.That((await http.PostAsync("/callbacks/contracts", null)).StatusCode)
            .IsEqualTo(HttpStatusCode.Accepted);

        await grpc.UploadContractAsync(
            new UploadContractRequest
            {
                ChannelName = "bank-a",
                Document = OpenApiSamples.OpenApi30WithPath("GET", "/callbacks/other"),
            },
            ManagementApi.Call());

        await SetRecipientQueueAsync(grpc, "bank-a", version: 2);

        await grpc.PublishContractVersionAsync(
            new PublishContractVersionRequest { ChannelName = "bank-a", Version = 2 },
            ManagementApi.Call());

        var activated = await grpc.ActivateContractVersionAsync(
            new ActivateContractVersionRequest { ChannelName = "bank-a", Version = 2 },
            ManagementApi.Call());

        await Assert.That(activated.Succeeded).IsTrue();
        await Assert.That((await http.PostAsync("/callbacks/contracts", null)).StatusCode)
            .IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That((await http.GetAsync("/callbacks/other")).StatusCode)
            .IsEqualTo(HttpStatusCode.Accepted);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
        var active = await db.ContractVersions.SingleAsync(version => version.Status == ContractVersionStatus.Active);

        await Assert.That(active.Version).IsEqualTo(2);
    }

    [Test]
    public async Task Published_version_cannot_be_published_again()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var http = ManagementApi.CreateClient(factory);
        await CreateChannelAsync(http, "bank-a");

        using var channel = ManagementApi.CreateChannel(factory);
        var grpc = new ManagementService.ManagementServiceClient(channel);

        await grpc.UploadContractAsync(
            new UploadContractRequest
            {
                ChannelName = "bank-a",
                Document = OpenApiSamples.OpenApi30WithPath("POST", "/callbacks/contracts"),
            },
            ManagementApi.Call());

        await SetRecipientQueueAsync(grpc, "bank-a");

        await grpc.PublishContractVersionAsync(
            new PublishContractVersionRequest { ChannelName = "bank-a", Version = 1 },
            ManagementApi.Call());

        var second = await grpc.PublishContractVersionAsync(
            new PublishContractVersionRequest { ChannelName = "bank-a", Version = 1 },
            ManagementApi.Call());

        await Assert.That(second.Succeeded).IsFalse();
        await Assert.That(second.Problems.Any(problem => problem.Code == "version")).IsTrue();
    }
}
