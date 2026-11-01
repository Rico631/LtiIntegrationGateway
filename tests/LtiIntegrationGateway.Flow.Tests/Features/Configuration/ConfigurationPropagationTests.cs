using System.Net;
using LtiIntegrationGateway.Contracts.Management.V1;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Contracts;
using LtiIntegrationGateway.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace LtiIntegrationGateway.Tests.Features.Configuration;

/// <summary>
/// Применение конфигурации экземплярами потоков: маршрут появляется без перезапуска, сбой
/// применения сохраняет предыдущую конфигурацию, перезапуск восстанавливает состояние.
/// </summary>
public class ConfigurationPropagationTests
{
    private const string Integration = FlowHostFactory.DefaultIntegration;
    private const string Channel = "callbacks";
    private const string Route = "/callbacks/contracts";

    private static readonly TimeSpan ApplyDeadline = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DeliveryDeadline = TimeSpan.FromSeconds(15);

    private static string Document(string path = Route) =>
        OpenApiSamples.OpenApi30(("POST", path, "receiveContract"));

    [Test]
    public async Task New_route_is_served_within_five_seconds_without_restart()
    {
        using var pair = new GatewayPair(ManagementApi.Settings());

        using (var flow = pair.Flow.CreateClient())
        {
            await Assert.That((await flow.PostAsync(Route, null)).StatusCode)
                .IsEqualTo(HttpStatusCode.NotFound);
        }

        await ActivateContractAsync(pair.Management);

        using var client = pair.Flow.CreateClient();
        var status = await WaitForStatusAsync(client, Route, HttpStatusCode.Accepted, ApplyDeadline);

        await Assert.That(status).IsEqualTo(HttpStatusCode.Accepted);
    }

    [Test]
    public async Task Failed_application_keeps_previous_routes_and_reports_error()
    {
        using var pair = new GatewayPair(ManagementApi.Settings());
        await ActivateContractAsync(pair.Management);
        await pair.ApplyAsync();

        using (var client = pair.Flow.CreateClient())
        {
            await Assert.That((await client.PostAsync(Route, null)).StatusCode)
                .IsEqualTo(HttpStatusCode.Accepted);
        }

        var desired = await AddConflictingActiveContractAsync(pair.Database);

        // Применение отклоняется проверкой: каталог остаётся прежним, applied-версия не растёт.
        await pair.ApplyAsync();

        using (var client = pair.Flow.CreateClient())
        {
            await Assert.That((await client.PostAsync(Route, null)).StatusCode)
                .IsEqualTo(HttpStatusCode.Accepted);
        }

        var state = await SingleStateAsync(pair.Database);

        await Assert.That(state.AppliedVersion).IsLessThan(desired);
        await Assert.That(state.ApplyError).IsNotNull();
        await Assert.That(state.ApplyError!).Contains("conflict");

        // Устранение причины: конфликтующая версия снята, применение проходит без перезапуска.
        await using (var db = pair.Database.CreateContext())
        {
            var conflicting = await db.ContractVersions.SingleAsync(
                version => version.InboundChannel!.Name == "conflicting");
            conflicting.Status = ContractVersionStatus.Superseded;

            await IntegrationRegistry.BumpAsync(db, Integration);
            await db.SaveChangesAsync();
        }

        await pair.ApplyAsync();

        var recovered = await SingleStateAsync(pair.Database);

        await Assert.That(recovered.AppliedVersion).IsGreaterThanOrEqualTo(desired);
        await Assert.That(recovered.ApplyError).IsNull();
    }

    [Test]
    public async Task Restart_restores_applied_state_without_manual_actions()
    {
        using var database = new PostgresTestDatabase();
        database.Migrate();
        using var management = ManagementHostFactory.WithExistingStorage(
            database.ConnectionString,
            ManagementApi.Settings());

        await ActivateContractAsync(management);

        using (var first = FlowHostFactory.WithExistingStorage(database.ConnectionString))
        {
            using var client = first.CreateClient();

            await Assert.That(await WaitForStatusAsync(client, Route, HttpStatusCode.Accepted, ApplyDeadline))
                .IsEqualTo(HttpStatusCode.Accepted);
        }

        var desired = await DesiredVersionAsync(database);

        using (var second = FlowHostFactory.WithExistingStorage(database.ConnectionString))
        {
            using var client = second.CreateClient();

            await Assert.That(await WaitForStatusAsync(client, Route, HttpStatusCode.Accepted, ApplyDeadline))
                .IsEqualTo(HttpStatusCode.Accepted);
        }

        var state = await SingleStateAsync(database);

        await Assert.That(state.AppliedVersion).IsEqualTo(desired);
        await Assert.That(state.ApplyError).IsNull();
    }

    [Test]
    public async Task Two_instances_apply_configuration_independently()
    {
        using var database = new PostgresTestDatabase();
        database.Migrate();
        using var management = ManagementHostFactory.WithExistingStorage(
            database.ConnectionString,
            ManagementApi.Settings());

        await ActivateContractAsync(management);

        using var first = FlowHostFactory.WithExistingStorage(
            database.ConnectionString,
            new Dictionary<string, string?> { ["Gateway:InstanceId"] = "replica-a" });
        using var second = FlowHostFactory.WithExistingStorage(
            database.ConnectionString,
            new Dictionary<string, string?> { ["Gateway:InstanceId"] = "replica-b" });

        using var firstClient = first.CreateClient();
        using var secondClient = second.CreateClient();

        await Assert.That(await WaitForStatusAsync(firstClient, Route, HttpStatusCode.Accepted, ApplyDeadline))
            .IsEqualTo(HttpStatusCode.Accepted);
        await Assert.That(await WaitForStatusAsync(secondClient, Route, HttpStatusCode.Accepted, ApplyDeadline))
            .IsEqualTo(HttpStatusCode.Accepted);

        var desired = await DesiredVersionAsync(database);

        await using var db = database.CreateContext();
        var states = await db.FlowInstanceStates
            .AsNoTracking()
            .Where(state => state.IntegrationId == Integration)
            .ToListAsync();

        await Assert.That(states.Count).IsEqualTo(2);
        await Assert.That(states.All(state => state.AppliedVersion == desired)).IsTrue();
        await Assert.That(states.Select(state => state.InstanceId).OrderBy(id => id).ToArray())
            .IsEquivalentTo(new[] { "replica-a", "replica-b" });
    }

    private static async Task ActivateContractAsync(ManagementHostFactory management)
    {
        using var http = ManagementApi.CreateClient(management);
        await ContractOperations.CreateOutboundChannelAsync(http, Channel);

        using var channel = ManagementApi.CreateChannel(management);
        var grpc = new ManagementService.ManagementServiceClient(channel);

        await ContractOperations.UploadAsync(grpc, Document(), Channel);
        await ContractOperations.SetVersionQueueAsync(grpc, 1, channelName: Channel);

        var published = await ContractOperations.PublishAsync(grpc, 1, Channel);
        await Assert.That(published.Succeeded).IsTrue();

        var activated = await ContractOperations.ActivateAsync(grpc, 1, Channel);
        await Assert.That(activated.Succeeded).IsTrue();
    }

    /// <summary>
    /// Добавляет активную версию другого канала с тем же маршрутом в обход управляющих проверок:
    /// так проверяется поведение экземпляра при сбое применения.
    /// </summary>
    private static async Task<long> AddConflictingActiveContractAsync(PostgresTestDatabase database)
    {
        await using var db = database.CreateContext();

        var inbound = new InboundChannel
        {
            Id = Guid.NewGuid(),
            Name = "conflicting",
            IntegrationId = Integration,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var version = new ContractVersion
        {
            Id = Guid.NewGuid(),
            InboundChannelId = inbound.Id,
            Version = 1,
            SourceFormat = ContractSourceFormat.OpenApi30,
            Document = "{}",
            Status = ContractVersionStatus.Active,
            RecipientQueue = ContractOperations.VersionQueue,
            AuthType = ChannelAuthType.None,
            CreatedAt = DateTimeOffset.UtcNow,
            Operations =
            {
                new ContractOperation
                {
                    Id = Guid.NewGuid(),
                    HttpMethod = "POST",
                    Template = Route,
                    OperationId = "conflicting",
                },
            },
        };

        db.InboundChannels.Add(inbound);
        db.ContractVersions.Add(version);

        var desired = await IntegrationRegistry.BumpAsync(db, Integration);
        await db.SaveChangesAsync();

        return desired;
    }

    private static async Task<long> DesiredVersionAsync(PostgresTestDatabase database)
    {
        await using var db = database.CreateContext();

        return await db.Integrations
            .AsNoTracking()
            .Where(integration => integration.IntegrationId == Integration)
            .Select(integration => integration.ConfigurationVersion)
            .SingleAsync();
    }

    private static async Task<LtiIntegrationGateway.Infrastructure.Persistence.Entities.Flow.FlowInstanceState>
        SingleStateAsync(PostgresTestDatabase database)
    {
        await using var db = database.CreateContext();

        return await db.FlowInstanceStates
            .AsNoTracking()
            .SingleAsync(state => state.IntegrationId == Integration);
    }

    private static async Task<HttpStatusCode> WaitForStatusAsync(
        HttpClient client,
        string path,
        HttpStatusCode expected,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        var status = HttpStatusCode.NotFound;

        while (DateTimeOffset.UtcNow < deadline)
        {
            status = (await client.PostAsync(path, null)).StatusCode;

            if (status == expected)
            {
                return status;
            }

            await Task.Delay(100);
        }

        return status;
    }
}
