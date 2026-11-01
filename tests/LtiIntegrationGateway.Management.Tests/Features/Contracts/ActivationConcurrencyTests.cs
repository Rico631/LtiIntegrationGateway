using LtiIntegrationGateway.Contracts.Management.V1;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Contracts;
using LtiIntegrationGateway.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LtiIntegrationGateway.Tests.Features.Contracts;

/// <summary>
/// Гонки активаций при нескольких репликах сервиса управления: действующей остаётся ровно одна
/// ревизия канала и одна версия контракта, проигравшая операция сообщает о конфликте.
/// </summary>
public class ActivationConcurrencyTests
{
    private const string Integration = "bank-a";
    private const string Channel = "callbacks";
    private const string Queue = "dpv.inbox";

    private static ChannelSettings ChannelSettings(string baseUrl) => new()
    {
        BaseUrl = baseUrl,
        AuthType = "none",
        PerAttemptTimeoutMs = 5_000,
        OverallCapMs = 30_000,
        RetryPolicy = { new LtiIntegrationGateway.Contracts.Management.V1.RetryRule { Match = "transport_error", DelaysMs = { 1_000 } } },
    };

    [Test]
    public async Task Concurrent_activations_of_contract_versions_leave_one_active_version()
    {
        using var factory = ManagementHostFactory.WithStorage(ManagementApi.Settings());
        using var channel = ManagementApi.CreateChannel(factory);
        var grpc = new ManagementService.ManagementServiceClient(channel);

        var created = await grpc.CreateInboundChannelAsync(
            new CreateInboundChannelRequest
            {
                Name = Channel,
                Integration = Integration,
                Document = OpenApiSamples.OpenApi30(("POST", "/callbacks/contracts", "receiveContract")),
            },
            ManagementApi.Call());

        await Assert.That(created.Succeeded).IsTrue();

        await grpc.UploadInboundContractVersionAsync(
            new UploadContractRequest
            {
                ChannelName = Channel,
                Integration = Integration,
                Document = OpenApiSamples.OpenApi30(("POST", "/callbacks/contracts/v2", "receiveContractV2")),
            },
            ManagementApi.Call());

        foreach (var version in new[] { 1, 2 })
        {
            var settings = await grpc.UpdateInboundContractVersionSettingsAsync(
                new UpdateContractVersionSettingsRequest
                {
                    ChannelName = Channel,
                    Version = version,
                    Integration = Integration,
                    Settings = new ContractVersionSettings { RecipientQueue = Queue },
                },
                ManagementApi.Call());

            await Assert.That(settings.Succeeded).IsTrue();

            var publish = await grpc.PublishInboundContractVersionAsync(
                new PublishContractVersionRequest
                {
                    ChannelName = Channel,
                    Version = version,
                    Integration = Integration,
                },
                ManagementApi.Call());

            await Assert.That(publish.Succeeded).IsTrue();
        }

        var activations = await Task.WhenAll(
            ActivateAsync(grpc, 1),
            ActivateAsync(grpc, 2));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();

        var active = await db.ContractVersions
            .Where(version => version.InboundChannel!.IntegrationId == Integration &&
                version.InboundChannel!.Name == Channel &&
                version.Status == ContractVersionStatus.Active)
            .ToListAsync();

        await Assert.That(active.Count).IsEqualTo(1);

        foreach (var rejected in activations.Where(result => !result.Succeeded))
        {
            await Assert.That(rejected.Problems.Any(problem => problem.Code == "conflict")).IsTrue();
        }
    }

    [Test]
    public async Task Concurrent_activations_of_channel_revisions_leave_one_active_revision()
    {
        using var factory = ManagementHostFactory.WithStorage(ManagementApi.Settings());
        using var channel = ManagementApi.CreateChannel(factory);
        var grpc = new ManagementService.ManagementServiceClient(channel);

        await grpc.CreateOutboundChannelAsync(
            new CreateChannelRequest
            {
                Name = "payments",
                Integration = Integration,
                Settings = ChannelSettings("https://bank-a.example/v1"),
            },
            ManagementApi.Call());

        foreach (var revision in new[] { 1, 2 })
        {
            if (revision == 2)
            {
                await grpc.UpdateOutboundChannelDraftAsync(
                    new UpdateChannelDraftRequest
                    {
                        ChannelName = "payments",
                        Integration = Integration,
                        Settings = ChannelSettings("https://bank-a.example/v2"),
                    },
                    ManagementApi.Call());
            }

            var publish = await grpc.PublishOutboundChannelRevisionAsync(
                new PublishChannelRevisionRequest
                {
                    ChannelName = "payments",
                    Revision = revision,
                    Integration = Integration,
                },
                ManagementApi.Call());

            await Assert.That(publish.Succeeded).IsTrue();
        }

        var activations = await Task.WhenAll(
            ActivateChannelAsync(grpc, 1),
            ActivateChannelAsync(grpc, 2));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();

        var active = await db.ChannelRevisions
            .Where(entity => entity.OutboundChannel!.IntegrationId == Integration &&
                entity.OutboundChannel!.Name == "payments" &&
                entity.Status == ChannelRevisionStatus.Active)
            .ToListAsync();

        await Assert.That(active.Count).IsEqualTo(1);

        foreach (var rejected in activations.Where(result => !result.Succeeded))
        {
            await Assert.That(rejected.Problems.Any(problem => problem.Code == "conflict")).IsTrue();
        }
    }

    private static Task<ContractOperationResponse> ActivateAsync(
        ManagementService.ManagementServiceClient client,
        int version) =>
        client.ActivateInboundContractVersionAsync(
            new ActivateContractVersionRequest
            {
                ChannelName = Channel,
                Version = version,
                Integration = Integration,
            },
            ManagementApi.Call()).ResponseAsync;

    private static Task<ChannelOperationResponse> ActivateChannelAsync(
        ManagementService.ManagementServiceClient client,
        int revision) =>
        client.ActivateOutboundChannelRevisionAsync(
            new ActivateChannelRevisionRequest
            {
                ChannelName = "payments",
                Revision = revision,
                Integration = Integration,
            },
            ManagementApi.Call()).ResponseAsync;
}
