using LtiIntegrationGateway.Contracts.Management.V1;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Contracts;
using LtiIntegrationGateway.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static LtiIntegrationGateway.Tests.TestSupport.ContractOperations;

namespace LtiIntegrationGateway.Tests.Features.Contracts;

/// <summary>
/// Проверка очереди получателя версии контракта при публикации: допустим только формат
/// <c>&lt;product_id&gt;.inbox</c> (ADR §10.2).
/// </summary>
public class ContractPublishQueueTests
{
    private static string DocumentWithOperation =>
        Document(("POST", "/callbacks/contracts", "receiveContract"));

    [Test]
    public async Task Publish_with_product_inbox_queue_succeeds()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var http = ManagementApi.CreateClient(factory);
        await CreateOutboundChannelAsync(http);

        using var OutboundChannel = ManagementApi.CreateChannel(factory);
        var grpc = new ManagementService.ManagementServiceClient(OutboundChannel);

        await UploadAsync(grpc, DocumentWithOperation);
        await SetVersionQueueAsync(grpc, 1, "dpv.inbox");

        var published = await PublishAsync(grpc, 1);

        await Assert.That(published.Succeeded).IsTrue();
        await Assert.That(published.Problems.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Publish_with_foreign_queue_is_rejected_without_operations()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var http = ManagementApi.CreateClient(factory);
        await CreateOutboundChannelAsync(http);

        using var OutboundChannel = ManagementApi.CreateChannel(factory);
        var grpc = new ManagementService.ManagementServiceClient(OutboundChannel);

        await UploadAsync(grpc, DocumentWithOperation);
        await SetVersionQueueAsync(grpc, 1, "inbound.queue");

        var published = await PublishAsync(grpc, 1);

        await Assert.That(published.Succeeded).IsFalse();
        await Assert.That(published.Problems.Any(problem => problem.Code == "queue")).IsTrue();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
        var version = await db.ContractVersions
            .Include(entity => entity.Operations)
            .SingleAsync();

        // Публикация отклонена до извлечения операций: версия остаётся черновиком, операций нет.
        await Assert.That(version.Status).IsEqualTo(ContractVersionStatus.Draft);
        await Assert.That(version.Operations.Count).IsEqualTo(0);
    }
}
