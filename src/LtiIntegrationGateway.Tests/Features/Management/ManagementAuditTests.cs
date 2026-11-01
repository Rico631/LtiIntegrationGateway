using System.Net;
using Grpc.Core;
using LtiIntegrationGateway.Contracts.Management.V1;
using LtiIntegrationGateway.Infrastructure.Management;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Management;
using LtiIntegrationGateway.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using ProtoRetryRule = LtiIntegrationGateway.Contracts.Management.V1.RetryRule;

namespace LtiIntegrationGateway.Tests.Features.Management;

/// <summary>Аудит управляющих операций: чтение канала, отсутствие канала и отказы валидации.</summary>
public class ManagementAuditTests
{
    private const string ChannelName = "bank-a";

    private static ChannelSettings Settings() => new()
    {
        BaseUrl = "https://bank-a.example",
        AuthType = "none",
        PerAttemptTimeoutMs = 5000,
        OverallCapMs = 30000,
        RetryPolicy = { new ProtoRetryRule { Match = "transport_error", DelaysMs = { 100 } } },
    };

    private static async Task CreateOutboundChannelAsync(GatewayWebApplicationFactory factory)
    {
        using var OutboundChannel = ManagementApi.CreateChannel(factory);
        var grpc = new ManagementService.ManagementServiceClient(OutboundChannel);

        var response = await grpc.CreateOutboundChannelAsync(
            new CreateChannelRequest { Name = ChannelName, Settings = Settings() },
            ManagementApi.Call());

        await Assert.That(response.Succeeded).IsTrue();
    }

    private static ManagementAuditEntry[] Audit(GatewayWebApplicationFactory factory, string operation)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();

        return db.ManagementAuditEntries.Where(entry => entry.Operation == operation).ToArray();
    }

    [Test]
    public async Task GetChannel_read_is_audited()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        await CreateOutboundChannelAsync(factory);

        using var client = ManagementApi.CreateClient(factory);
        var response = await client.GetAsync($"/v1/outbound-channels/{ChannelName}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var entry = Audit(factory, "GetChannel").Single();

        await Assert.That(entry.Target).IsEqualTo(ChannelName);
        await Assert.That(entry.Result).IsEqualTo(ManagementAuditResults.Succeeded);
    }

    [Test]
    public async Task Unknown_channel_is_not_found_and_audited()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var client = ManagementApi.CreateClient(factory);

        var cases = new (string Method, string Path, string? Body, string Operation)[]
        {
            ("GET", "/v1/outbound-channels/missing", null, "GetChannel"),
            ("PATCH", "/v1/outbound-channels/missing/draft", "{}", "UpdateChannelDraft"),
            ("POST", "/v1/outbound-channels/missing/revisions/1/publish", null, "PublishChannelRevision"),
            ("POST", "/v1/outbound-channels/missing/revisions/1/activate", null, "ActivateChannelRevision"),
            ("POST", "/v1/outbound-channels/missing/rollback", null, "RollbackChannelRevision"),
        };

        foreach (var item in cases)
        {
            using var request = new HttpRequestMessage(new HttpMethod(item.Method), item.Path);

            if (item.Body is not null)
            {
                request.Content = ManagementApi.Json(item.Body);
            }

            var response = await client.SendAsync(request);

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

            var entry = Audit(factory, item.Operation)
                .Single(candidate => candidate.Target == "missing");

            await Assert.That(entry.Result).IsEqualTo(ManagementAuditResults.Rejected);
        }
    }

    [Test]
    public async Task Unknown_channel_returns_not_found_over_grpc()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var OutboundChannel = ManagementApi.CreateChannel(factory);
        var grpc = new ManagementService.ManagementServiceClient(OutboundChannel);

        var exception = await Assert.ThrowsAsync<RpcException>(async () =>
            await grpc.GetOutboundChannelAsync(
                new GetChannelRequest { ChannelName = "missing" },
                ManagementApi.Call()));

        await Assert.That(exception!.StatusCode).IsEqualTo(StatusCode.NotFound);
    }

    [Test]
    public async Task Version_settings_validation_failures_are_audited()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var OutboundChannel = ManagementApi.CreateChannel(factory);
        var grpc = new ManagementService.ManagementServiceClient(OutboundChannel);

        var missingSettings = await grpc.UpdateInboundContractVersionSettingsAsync(
            new UpdateContractVersionSettingsRequest { ChannelName = ChannelName, Version = 1 },
            ManagementApi.Call());

        var unknownAuthType = await grpc.UpdateInboundContractVersionSettingsAsync(
            new UpdateContractVersionSettingsRequest
            {
                ChannelName = ChannelName,
                Version = 1,
                Settings = new ContractVersionSettings { AuthType = "oauth2" },
            },
            ManagementApi.Call());

        await Assert.That(missingSettings.Succeeded).IsFalse();
        await Assert.That(missingSettings.Problems.Any(problem => problem.Code == "settings")).IsTrue();
        await Assert.That(unknownAuthType.Succeeded).IsFalse();
        await Assert.That(unknownAuthType.Problems.Any(problem => problem.Code == "auth_type")).IsTrue();

        var entries = Audit(factory, "UpdateContractVersionSettings");

        await Assert.That(entries.Length).IsEqualTo(2);
        await Assert.That(entries.All(entry => entry.Target == $"{ChannelName}:v1")).IsTrue();
        await Assert.That(entries.All(entry => entry.Result == ManagementAuditResults.Rejected)).IsTrue();
    }

    [Test]
    public async Task Health_read_is_audited_once()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        await CreateOutboundChannelAsync(factory);

        using var client = ManagementApi.CreateClient(factory);
        var response = await client.GetAsync($"/v1/outbound-channels/{ChannelName}/health");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var entries = Audit(factory, "GetChannelHealth");

        await Assert.That(entries.Length).IsEqualTo(1);
        await Assert.That(entries[0].Result).IsEqualTo(ManagementAuditResults.Succeeded);
    }
}
