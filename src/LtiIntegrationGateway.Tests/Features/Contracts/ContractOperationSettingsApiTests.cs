using System.Net;
using System.Text.Json;
using Grpc.Core;
using Grpc.Net.Client;
using LtiIntegrationGateway.Contracts.Management.V1;
using LtiIntegrationGateway.Infrastructure.Management;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities;
using LtiIntegrationGateway.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static LtiIntegrationGateway.Tests.TestSupport.ContractOperations;

namespace LtiIntegrationGateway.Tests.Features.Contracts;

/// <summary>Чтение и изменение настроек операции через управляющий API (ADR §15.2).</summary>
public class ContractOperationSettingsApiTests
{
    private const string OperationQueue = "other.inbox";

    private static async Task<ManagementService.ManagementServiceClient> PreparePublishedAsync(
        GatewayWebApplicationFactory factory,
        GrpcChannel channel)
    {
        using var http = ManagementApi.CreateClient(factory);
        await CreateChannelAsync(http);

        var grpc = new ManagementService.ManagementServiceClient(channel);

        await UploadAsync(grpc, Document(("POST", "/callbacks/contracts", "receiveContract")));
        await SetVersionQueueAsync(grpc, 1);
        await PublishAsync(grpc, 1);

        return grpc;
    }

    [Test]
    public async Task Operation_settings_are_read_and_changed_via_grpc()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var channel = ManagementApi.CreateChannel(factory);
        var grpc = await PreparePublishedAsync(factory, channel);

        var before = await ReadOperationsAsync(grpc, 1);

        await Assert.That(before.Operations.Count).IsEqualTo(1);
        await Assert.That(before.Operations[0].OperationId).IsEqualTo("receiveContract");
        await Assert.That(before.Operations[0].Enabled).IsTrue();
        await Assert.That(before.Operations[0].RecipientQueue).IsEmpty();
        await Assert.That(before.Operations[0].RateLimitPermit).IsEqualTo(0);
        await Assert.That(before.Operations[0].RateLimitWindowSeconds).IsEqualTo(0);

        var update = await PatchAsync(
            grpc,
            1,
            "receiveContract",
            enabled: false,
            recipientQueue: OperationQueue,
            rateLimitPermit: 5,
            rateLimitWindowSeconds: 20);

        await Assert.That(update.Succeeded).IsTrue();

        var after = await ReadOperationsAsync(grpc, 1);

        await Assert.That(after.Operations[0].Enabled).IsFalse();
        await Assert.That(after.Operations[0].RecipientQueue).IsEqualTo(OperationQueue);
        await Assert.That(after.Operations[0].RateLimitPermit).IsEqualTo(5);
        await Assert.That(after.Operations[0].RateLimitWindowSeconds).IsEqualTo(20);

        var version = await grpc.GetContractVersionAsync(
            new GetContractVersionRequest { ChannelName = ChannelName, Version = 1 },
            ManagementApi.Call());

        await Assert.That(version.Operations[0].Enabled).IsFalse();
        await Assert.That(version.Operations[0].RecipientQueue).IsEqualTo(OperationQueue);
        await Assert.That(version.Operations[0].RateLimitPermit).IsEqualTo(5);
    }

    [Test]
    public async Task Operation_settings_are_changed_via_rest_patch()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var channel = ManagementApi.CreateChannel(factory);
        var grpc = await PreparePublishedAsync(factory, channel);
        using var http = ManagementApi.CreateClient(factory);

        var response = await http.PatchAsync(
            $"/v1/channels/{ChannelName}/contracts/1/operations/receiveContract",
            ManagementApi.Json(
                $$"""
                { "enabled": false, "recipientQueue": "{{OperationQueue}}", "rateLimitPermit": 5, "rateLimitWindowSeconds": 20 }
                """));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using (var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            await Assert.That(document.RootElement.GetProperty("succeeded").GetBoolean()).IsTrue();
        }

        var read = await http.GetAsync($"/v1/channels/{ChannelName}/contracts/1");

        await Assert.That(read.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using (var document = JsonDocument.Parse(await read.Content.ReadAsStringAsync()))
        {
            var operation = document.RootElement.GetProperty("operations")[0];

            await Assert.That(operation.GetProperty("enabled").GetBoolean()).IsFalse();
            await Assert.That(operation.GetProperty("recipientQueue").GetString()).IsEqualTo(OperationQueue);
            await Assert.That(operation.GetProperty("rateLimitPermit").GetInt32()).IsEqualTo(5);
            await Assert.That(operation.GetProperty("rateLimitWindowSeconds").GetInt32()).IsEqualTo(20);
        }
    }

    [Test]
    public async Task Partial_change_keeps_other_settings()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var channel = ManagementApi.CreateChannel(factory);
        var grpc = await PreparePublishedAsync(factory, channel);

        var first = await PatchAsync(
            grpc,
            1,
            "receiveContract",
            recipientQueue: OperationQueue,
            rateLimitPermit: 5,
            rateLimitWindowSeconds: 20);

        await Assert.That(first.Succeeded).IsTrue();

        var second = await PatchAsync(grpc, 1, "receiveContract", enabled: false);

        await Assert.That(second.Succeeded).IsTrue();

        var after = await ReadOperationsAsync(grpc, 1);

        await Assert.That(after.Operations[0].Enabled).IsFalse();
        await Assert.That(after.Operations[0].RecipientQueue).IsEqualTo(OperationQueue);
        await Assert.That(after.Operations[0].RateLimitPermit).IsEqualTo(5);
        await Assert.That(after.Operations[0].RateLimitWindowSeconds).IsEqualTo(20);

        var cleared = await PatchAsync(grpc, 1, "receiveContract", recipientQueue: "");

        await Assert.That(cleared.Succeeded).IsTrue();

        var afterClear = await ReadOperationsAsync(grpc, 1);

        await Assert.That(afterClear.Operations[0].RecipientQueue).IsEmpty();
        await Assert.That(afterClear.Operations[0].RateLimitPermit).IsEqualTo(5);
    }

    [Test]
    public async Task Superseded_version_rejects_changes_with_version_problem()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var http = ManagementApi.CreateClient(factory);
        await CreateChannelAsync(http);

        using var channel = ManagementApi.CreateChannel(factory);
        var grpc = new ManagementService.ManagementServiceClient(channel);

        await PublishAndActivateAsync(grpc, Document(("POST", "/callbacks/contracts", "receiveContract")), 1);
        await PublishAndActivateAsync(grpc, Document(("POST", "/callbacks/contracts", "receiveContract")), 2);

        var rejected = await PatchAsync(grpc, 1, "receiveContract", enabled: false);

        await Assert.That(rejected.Succeeded).IsFalse();
        await Assert.That(rejected.Problems.Any(problem => problem.Code == "version")).IsTrue();

        var after = await ReadOperationsAsync(grpc, 1);

        await Assert.That(after.Operations[0].Enabled).IsTrue();
    }

    [Test]
    public async Task Unknown_version_and_operation_return_not_found()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var channel = ManagementApi.CreateChannel(factory);
        var grpc = await PreparePublishedAsync(factory, channel);

        var unknownVersion = await CaptureAsync(() => PatchAsync(grpc, 99, "receiveContract", enabled: false));
        var unknownOperation = await CaptureAsync(() => PatchAsync(grpc, 1, "missing", enabled: false));

        await Assert.That(unknownVersion).IsEqualTo(StatusCode.NotFound);
        await Assert.That(unknownOperation).IsEqualTo(StatusCode.NotFound);

        using var http = ManagementApi.CreateClient(factory);

        var rest = await http.PatchAsync(
            $"/v1/channels/{ChannelName}/contracts/1/operations/missing",
            ManagementApi.Json("""{ "enabled": false }"""));

        await Assert.That(rest.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Operation_without_operation_id_is_not_addressable()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var http = ManagementApi.CreateClient(factory);
        await CreateChannelAsync(http);

        using var channel = ManagementApi.CreateChannel(factory);
        var grpc = new ManagementService.ManagementServiceClient(channel);

        await UploadAsync(grpc, Document(("POST", "/callbacks/contracts", null)));
        await SetVersionQueueAsync(grpc, 1);

        var published = await PublishAsync(grpc, 1);

        await Assert.That(published.Succeeded).IsTrue();

        var operations = await ReadOperationsAsync(grpc, 1);

        await Assert.That(operations.Operations.Count).IsEqualTo(1);
        await Assert.That(operations.Operations[0].OperationId).IsEmpty();

        // Операция без operationId управляющим API не адресуется: запрос изменения — NotFound.
        var grpcStatus = await CaptureAsync(() => PatchAsync(grpc, 1, "receiveContract", enabled: false));

        await Assert.That(grpcStatus).IsEqualTo(StatusCode.NotFound);

        var rest = await http.PatchAsync(
            $"/v1/channels/{ChannelName}/contracts/1/operations/receiveContract",
            ManagementApi.Json("""{ "enabled": false }"""));

        await Assert.That(rest.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Missing_fields_return_settings_problem()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var channel = ManagementApi.CreateChannel(factory);
        var grpc = await PreparePublishedAsync(factory, channel);

        var empty = await grpc.UpdateContractOperationSettingsAsync(
            new UpdateContractOperationSettingsRequest
            {
                ChannelName = ChannelName,
                Version = 1,
                OperationId = "receiveContract",
            },
            ManagementApi.Call());

        await Assert.That(empty.Succeeded).IsFalse();
        await Assert.That(empty.Problems.Any(problem => problem.Code == "settings")).IsTrue();
    }

    [Test]
    public async Task Invalid_operation_queue_is_rejected_and_settings_are_unchanged()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var channel = ManagementApi.CreateChannel(factory);
        var grpc = await PreparePublishedAsync(factory, channel);

        var rejected = await PatchAsync(grpc, 1, "receiveContract", recipientQueue: "inbound.queue");

        await Assert.That(rejected.Succeeded).IsFalse();
        await Assert.That(rejected.Problems.Any(problem => problem.Code == "queue")).IsTrue();

        var afterRejection = await ReadOperationsAsync(grpc, 1);

        await Assert.That(afterRejection.Operations[0].RecipientQueue).IsEmpty();

        var accepted = await PatchAsync(grpc, 1, "receiveContract", recipientQueue: "dpv.inbox");

        await Assert.That(accepted.Succeeded).IsTrue();

        var afterAcceptance = await ReadOperationsAsync(grpc, 1);

        await Assert.That(afterAcceptance.Operations[0].RecipientQueue).IsEqualTo("dpv.inbox");
    }

    [Test]
    public async Task Non_positive_rate_limit_returns_rate_limit_problem()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var channel = ManagementApi.CreateChannel(factory);
        var grpc = await PreparePublishedAsync(factory, channel);

        var zeroPermit = await PatchAsync(grpc, 1, "receiveContract", rateLimitPermit: 0);
        var negativeWindow = await PatchAsync(grpc, 1, "receiveContract", rateLimitWindowSeconds: -1);

        await Assert.That(zeroPermit.Problems.Any(problem => problem.Code == "rate_limit")).IsTrue();
        await Assert.That(negativeWindow.Problems.Any(problem => problem.Code == "rate_limit")).IsTrue();

        var after = await ReadOperationsAsync(grpc, 1);

        await Assert.That(after.Operations[0].RateLimitPermit).IsEqualTo(0);
        await Assert.That(after.Operations[0].RateLimitWindowSeconds).IsEqualTo(0);
    }

    [Test]
    public async Task Change_is_recorded_in_audit()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var channel = ManagementApi.CreateChannel(factory);
        var grpc = await PreparePublishedAsync(factory, channel);

        await PatchAsync(grpc, 1, "receiveContract", enabled: false);
        await CaptureAsync(() => PatchAsync(grpc, 1, "missing", enabled: false));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
        var entries = await db.ManagementAuditEntries
            .Where(entry => entry.Operation == "UpdateContractOperationSettings")
            .ToListAsync();

        await Assert.That(entries.Any(entry =>
            entry.Target == $"{ChannelName}:v1:receiveContract" &&
            entry.Result == ManagementAuditResults.Succeeded)).IsTrue();
        await Assert.That(entries.Any(entry =>
            entry.Target == $"{ChannelName}:v1:missing" &&
            entry.Result == ManagementAuditResults.Rejected)).IsTrue();
    }

    [Test]
    public async Task Draft_version_has_no_operations_to_change()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var http = ManagementApi.CreateClient(factory);
        await CreateChannelAsync(http);

        using var channel = ManagementApi.CreateChannel(factory);
        var grpc = new ManagementService.ManagementServiceClient(channel);

        // Операции извлекаются при публикации: у черновика их ещё нет.
        await UploadAsync(grpc, Document(("POST", "/callbacks/contracts", "receiveContract")));

        var operations = await ReadOperationsAsync(grpc, 1);

        await Assert.That(operations.Operations.Count).IsEqualTo(0);

        var draftChange = await CaptureAsync(() => PatchAsync(grpc, 1, "receiveContract", enabled: false));

        await Assert.That(draftChange).IsEqualTo(StatusCode.NotFound);
    }

    private static async Task<StatusCode?> CaptureAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (RpcException exception)
        {
            return exception.StatusCode;
        }

        return null;
    }
}
