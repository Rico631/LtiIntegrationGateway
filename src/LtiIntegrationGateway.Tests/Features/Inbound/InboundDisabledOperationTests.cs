using System.Net;
using Grpc.Net.Client;
using LtiIntegrationGateway.Contracts.Management.V1;
using LtiIntegrationGateway.Features.Inbound;
using LtiIntegrationGateway.Infrastructure.Management;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities;
using LtiIntegrationGateway.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static LtiIntegrationGateway.Tests.TestSupport.ContractOperations;

namespace LtiIntegrationGateway.Tests.Features.Inbound;

/// <summary>Отключённая операция: ответ 404, аудит и применение без перезапуска (ADR §7.3, §13).</summary>
public class InboundDisabledOperationTests
{
    private const string Path = "/callbacks/contracts";

    private static async Task<ManagementService.ManagementServiceClient> PrepareActiveVersionAsync(
        GatewayWebApplicationFactory factory,
        GrpcChannel channel,
        bool withApiKeyAuth = false,
        string path = Path)
    {
        using var http = ManagementApi.CreateClient(factory);
        await CreateChannelAsync(http);

        var grpc = new ManagementService.ManagementServiceClient(channel);

        await UploadAsync(grpc, Document(("POST", path, "receiveContract")));

        if (withApiKeyAuth)
        {
            var settings = await grpc.UpdateContractVersionSettingsAsync(
                new UpdateContractVersionSettingsRequest
                {
                    ChannelName = ChannelName,
                    Version = 1,
                    Settings = new ContractVersionSettings
                    {
                        RecipientQueue = VersionQueue,
                        AuthType = "api_key",
                        ConfigKey = "bank-a.inbound",
                        HeaderName = "X-Bank-Key",
                    },
                },
                ManagementApi.Call());

            await Assert.That(settings.Succeeded).IsTrue();
        }
        else
        {
            await SetVersionQueueAsync(grpc, 1);
        }

        var published = await PublishAsync(grpc, 1);

        await Assert.That(published.Succeeded).IsTrue();

        var activated = await ActivateAsync(grpc, 1);

        await Assert.That(activated.Succeeded).IsTrue();

        return grpc;
    }

    [Test]
    public async Task Long_route_template_is_truncated_in_audit_target()
    {
        var longPath = "/callbacks/" + new string('a', 300);

        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var channel = ManagementApi.CreateChannel(factory);
        var grpc = await PrepareActiveVersionAsync(factory, channel, path: longPath);

        var disabled = await PatchAsync(grpc, 1, "receiveContract", enabled: false);

        await Assert.That(disabled.Succeeded).IsTrue();

        using var http = ManagementApi.CreateClient(factory);

        await Assert.That((await http.PostAsync(longPath, null)).StatusCode)
            .IsEqualTo(HttpStatusCode.NotFound);

        var entry = await SingleDisabledOperationAuditAsync(factory);

        await Assert.That(entry.Target.Length).IsEqualTo(ManagementAuditWriter.MaxTargetLength);
        await Assert.That(entry.Target).IsEqualTo($"{ChannelName}:POST {longPath}"[..ManagementAuditWriter.MaxTargetLength]);
        await Assert.That(entry.Details).IsEqualTo(longPath);
    }

    [Test]
    public async Task Long_request_path_is_truncated_in_audit_details()
    {
        const string template = "/callbacks/{id}";
        var longPath = "/callbacks/" + new string('b', 2100);

        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var channel = ManagementApi.CreateChannel(factory);
        var grpc = await PrepareActiveVersionAsync(factory, channel, path: template);

        var disabled = await PatchAsync(grpc, 1, "receiveContract", enabled: false);

        await Assert.That(disabled.Succeeded).IsTrue();

        using var http = ManagementApi.CreateClient(factory);

        await Assert.That((await http.PostAsync(longPath, null)).StatusCode)
            .IsEqualTo(HttpStatusCode.NotFound);

        var entry = await SingleDisabledOperationAuditAsync(factory);

        await Assert.That(entry.Target).IsEqualTo($"{ChannelName}:POST {template}");
        await Assert.That(entry.Details!.Length).IsEqualTo(ManagementAuditWriter.MaxDetailsLength);
    }

    private static async Task<ManagementAuditEntry> SingleDisabledOperationAuditAsync(
        GatewayWebApplicationFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();

        return await db.ManagementAuditEntries
            .AsNoTracking()
            .SingleAsync(entry => entry.Operation == "InboundOperationDisabled");
    }

    [Test]
    public async Task Disabled_operation_answers_not_found_and_is_audited()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var channel = ManagementApi.CreateChannel(factory);
        var grpc = await PrepareActiveVersionAsync(factory, channel);

        using var http = ManagementApi.CreateClient(factory);

        var served = await http.PostAsync(Path, null);

        await Assert.That(served.StatusCode).IsEqualTo(HttpStatusCode.Accepted);

        var disabled = await PatchAsync(grpc, 1, "receiveContract", enabled: false);

        await Assert.That(disabled.Succeeded).IsTrue();

        // Тело отключённого вызова не читается и не проверяется: маршрут не объявляет схему тела,
        // поэтому обслуживаемый вызов с телом получил бы 400, а отключённый — 404.
        var rejected = await http.PostAsync(Path, ManagementApi.Json("""{ "contractNumber": "DKP-2" }"""));

        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();

        var audit = await db.ManagementAuditEntries
            .Where(entry => entry.Operation == "InboundOperationDisabled")
            .ToListAsync();

        await Assert.That(audit.Count).IsEqualTo(1);
        await Assert.That(audit[0].Target).IsEqualTo($"{ChannelName}:POST {Path}");
        await Assert.That(audit[0].Details).IsEqualTo(Path);
        await Assert.That(audit[0].Result).IsEqualTo(ManagementAuditResults.Rejected);

        // Тело отключённого вызова не читается и в журнал приёма не попадает: остаётся одна запись.
        var logs = await db.InboundMessageLogs.AsNoTracking().ToArrayAsync();

        await Assert.That(logs.Length).IsEqualTo(1);
    }

    [Test]
    public async Task Disabled_operation_does_not_authenticate()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var channel = ManagementApi.CreateChannel(factory);
        var grpc = await PrepareActiveVersionAsync(factory, channel, withApiKeyAuth: true);

        var disabled = await PatchAsync(grpc, 1, "receiveContract", enabled: false);

        await Assert.That(disabled.Succeeded).IsTrue();

        using var http = ManagementApi.CreateClient(factory);

        // Секрет входящего вызова в тестовой конфигурации не задан: проверка авторизации дала бы 503.
        var response = await http.PostAsync(Path, ManagementApi.Json("{}"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Enabling_operation_restores_serving_without_restart()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var channel = ManagementApi.CreateChannel(factory);
        var grpc = await PrepareActiveVersionAsync(factory, channel);

        using var http = ManagementApi.CreateClient(factory);

        await PatchAsync(grpc, 1, "receiveContract", enabled: false);

        await Assert.That((await http.PostAsync(Path, ManagementApi.Json("{}"))).StatusCode)
            .IsEqualTo(HttpStatusCode.NotFound);

        var enabled = await PatchAsync(grpc, 1, "receiveContract", enabled: true);

        await Assert.That(enabled.Succeeded).IsTrue();
        await Assert.That((await http.PostAsync(Path, null)).StatusCode)
            .IsEqualTo(HttpStatusCode.Accepted);
    }
}
