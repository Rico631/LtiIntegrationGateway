using LtiIntegrationGateway.Contracts.Management.V1;
using LtiIntegrationGateway.Tests.TestSupport;
using static LtiIntegrationGateway.Tests.TestSupport.ContractOperations;

namespace LtiIntegrationGateway.Tests.Features.Contracts;

/// <summary>Публикация версии контракта с настройками операций (ADR §7.3).</summary>
public class ContractOperationPublishTests
{
    private const string OperationQueue = "other.inbox";

    [Test]
    public async Task Operation_settings_are_transferred_to_next_version_by_operation_id()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var http = ManagementApi.CreateClient(factory);
        await CreateChannelAsync(http);

        using var channel = ManagementApi.CreateChannel(factory);
        var grpc = new ManagementService.ManagementServiceClient(channel);

        await UploadAsync(grpc, Document(("POST", "/callbacks/contracts", "receiveContract")));
        await SetVersionQueueAsync(grpc, 1);
        await PublishAsync(grpc, 1);

        var patched = await PatchAsync(
            grpc,
            version: 1,
            operationId: "receiveContract",
            recipientQueue: OperationQueue,
            rateLimitPermit: 5,
            rateLimitWindowSeconds: 20);

        await Assert.That(patched.Succeeded).IsTrue();

        await UploadAsync(
            grpc,
            Document(
                ("POST", "/callbacks/contracts", "receiveContract"),
                ("GET", "/callbacks/orders", "listOrders")));
        await SetVersionQueueAsync(grpc, 2);

        var published = await PublishAsync(grpc, 2);

        await Assert.That(published.Succeeded).IsTrue();

        var operations = await ReadOperationsAsync(grpc, 2);
        var transferred = operations.Operations.Single(operation => operation.Template == "/callbacks/contracts");

        await Assert.That(transferred.Enabled).IsTrue();
        await Assert.That(transferred.RecipientQueue).IsEqualTo(OperationQueue);
        await Assert.That(transferred.RateLimitPermit).IsEqualTo(5);
        await Assert.That(transferred.RateLimitWindowSeconds).IsEqualTo(20);

        var added = operations.Operations.Single(operation => operation.Template == "/callbacks/orders");

        await Assert.That(added.Enabled).IsTrue();
        await Assert.That(added.RecipientQueue).IsEmpty();
        await Assert.That(added.RateLimitPermit).IsEqualTo(0);
        await Assert.That(added.RateLimitWindowSeconds).IsEqualTo(0);
    }

    [Test]
    public async Task Settings_are_transferred_by_method_and_template_without_operation_id()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var http = ManagementApi.CreateClient(factory);
        await CreateChannelAsync(http);

        using var channel = ManagementApi.CreateChannel(factory);
        var grpc = new ManagementService.ManagementServiceClient(channel);

        await UploadAsync(grpc, Document(("POST", "/callbacks/contracts", "receiveContract")));
        await SetVersionQueueAsync(grpc, 1);
        await PublishAsync(grpc, 1);

        var patched = await PatchAsync(
            grpc,
            version: 1,
            operationId: "receiveContract",
            recipientQueue: OperationQueue,
            rateLimitPermit: 7,
            rateLimitWindowSeconds: 30);

        await Assert.That(patched.Succeeded).IsTrue();

        // Новая версия объявляет тот же маршрут без operationId: совпадение по методу и шаблону.
        await UploadAsync(grpc, Document(("POST", "/callbacks/contracts", null)));
        await SetVersionQueueAsync(grpc, 2);

        var published = await PublishAsync(grpc, 2);

        await Assert.That(published.Succeeded).IsTrue();

        var operations = await ReadOperationsAsync(grpc, 2);

        await Assert.That(operations.Operations.Count).IsEqualTo(1);
        await Assert.That(operations.Operations[0].OperationId).IsEmpty();
        await Assert.That(operations.Operations[0].RecipientQueue).IsEqualTo(OperationQueue);
        await Assert.That(operations.Operations[0].RateLimitPermit).IsEqualTo(7);
        await Assert.That(operations.Operations[0].RateLimitWindowSeconds).IsEqualTo(30);
    }

    [Test]
    public async Task Enabled_operation_without_effective_queue_rejects_publication()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var http = ManagementApi.CreateClient(factory);
        await CreateChannelAsync(http);

        using var channel = ManagementApi.CreateChannel(factory);
        var grpc = new ManagementService.ManagementServiceClient(channel);

        await UploadAsync(grpc, Document(("POST", "/callbacks/contracts", "receiveContract")));

        var published = await PublishAsync(grpc, 1);

        await Assert.That(published.Succeeded).IsFalse();
        await Assert.That(published.Problems.Any(problem => problem.Code == "queue")).IsTrue();

        var operations = await ReadOperationsAsync(grpc, 1);

        await Assert.That(operations.Operations.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Version_without_queue_is_rejected_even_when_all_operations_are_disabled()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var http = ManagementApi.CreateClient(factory);
        await CreateChannelAsync(http);

        using var channel = ManagementApi.CreateChannel(factory);
        var grpc = new ManagementService.ManagementServiceClient(channel);

        await UploadAsync(grpc, Document(("POST", "/callbacks/contracts", "receiveContract")));
        await SetVersionQueueAsync(grpc, 1);
        await PublishAsync(grpc, 1);

        var disabled = await PatchAsync(grpc, version: 1, operationId: "receiveContract", enabled: false);

        await Assert.That(disabled.Succeeded).IsTrue();

        // Очередь получателя в настройках версии обязательна всегда (ADR §15.2), даже если ни одна
        // операция не обслуживается.
        await UploadAsync(grpc, Document(("POST", "/callbacks/contracts", "receiveContract")));

        var published = await PublishAsync(grpc, 2);

        await Assert.That(published.Succeeded).IsFalse();
        await Assert.That(published.Problems.Any(problem => problem.Code == "queue")).IsTrue();

        var operations = await ReadOperationsAsync(grpc, 2);

        await Assert.That(operations.Operations.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Version_with_all_operations_disabled_and_queue_set_is_published()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var http = ManagementApi.CreateClient(factory);
        await CreateChannelAsync(http);

        using var channel = ManagementApi.CreateChannel(factory);
        var grpc = new ManagementService.ManagementServiceClient(channel);

        await UploadAsync(grpc, Document(("POST", "/callbacks/contracts", "receiveContract")));
        await SetVersionQueueAsync(grpc, 1);
        await PublishAsync(grpc, 1);

        var disabled = await PatchAsync(grpc, version: 1, operationId: "receiveContract", enabled: false);

        await Assert.That(disabled.Succeeded).IsTrue();

        await UploadAsync(grpc, Document(("POST", "/callbacks/contracts", "receiveContract")));
        await SetVersionQueueAsync(grpc, 2);

        var published = await PublishAsync(grpc, 2);

        await Assert.That(published.Succeeded).IsTrue();

        var operations = await ReadOperationsAsync(grpc, 2);

        await Assert.That(operations.Operations.Count).IsEqualTo(1);
        await Assert.That(operations.Operations[0].Enabled).IsFalse();
    }

    [Test]
    public async Task Operation_queue_override_does_not_replace_version_queue()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var http = ManagementApi.CreateClient(factory);
        await CreateChannelAsync(http);

        using var channel = ManagementApi.CreateChannel(factory);
        var grpc = new ManagementService.ManagementServiceClient(channel);

        await UploadAsync(grpc, Document(("POST", "/callbacks/contracts", "receiveContract")));
        await SetVersionQueueAsync(grpc, 1);
        await PublishAsync(grpc, 1);

        var patched = await PatchAsync(
            grpc,
            version: 1,
            operationId: "receiveContract",
            recipientQueue: OperationQueue);

        await Assert.That(patched.Succeeded).IsTrue();

        // Настройки операции переносятся в новую версию, но очередь версии всё равно обязательна.
        await UploadAsync(grpc, Document(("POST", "/callbacks/contracts", "receiveContract")));

        var published = await PublishAsync(grpc, 2);

        await Assert.That(published.Succeeded).IsFalse();
        await Assert.That(published.Problems.Any(problem => problem.Code == "queue")).IsTrue();

        await SetVersionQueueAsync(grpc, 2);

        var retried = await PublishAsync(grpc, 2);

        await Assert.That(retried.Succeeded).IsTrue();

        var operations = await ReadOperationsAsync(grpc, 2);

        await Assert.That(operations.Operations[0].RecipientQueue).IsEqualTo(OperationQueue);
    }

    [Test]
    public async Task Changed_operation_id_does_not_transfer_settings()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var http = ManagementApi.CreateClient(factory);
        await CreateChannelAsync(http);

        using var channel = ManagementApi.CreateChannel(factory);
        var grpc = new ManagementService.ManagementServiceClient(channel);

        await UploadAsync(grpc, Document(("POST", "/callbacks/contracts", "receiveContract")));
        await SetVersionQueueAsync(grpc, 1);
        await PublishAsync(grpc, 1);

        var patched = await PatchAsync(
            grpc,
            version: 1,
            operationId: "receiveContract",
            recipientQueue: OperationQueue,
            rateLimitPermit: 5,
            rateLimitWindowSeconds: 20);

        await Assert.That(patched.Succeeded).IsTrue();

        // operationId изменился при том же методе и шаблоне: это новая операция, настройки не переносятся.
        await UploadAsync(grpc, Document(("POST", "/callbacks/contracts", "receiveContractV2")));
        await SetVersionQueueAsync(grpc, 2);

        var published = await PublishAsync(grpc, 2);

        await Assert.That(published.Succeeded).IsTrue();

        var operations = await ReadOperationsAsync(grpc, 2);

        await Assert.That(operations.Operations.Count).IsEqualTo(1);
        await Assert.That(operations.Operations[0].OperationId).IsEqualTo("receiveContractV2");
        await Assert.That(operations.Operations[0].Enabled).IsTrue();
        await Assert.That(operations.Operations[0].RecipientQueue).IsEmpty();
        await Assert.That(operations.Operations[0].RateLimitPermit).IsEqualTo(0);
        await Assert.That(operations.Operations[0].RateLimitWindowSeconds).IsEqualTo(0);
    }
}
