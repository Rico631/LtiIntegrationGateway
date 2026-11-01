using LtiIntegrationGateway.Features.Outbound;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Outbound;
using LtiIntegrationGateway.Tests.TestSupport;

namespace LtiIntegrationGateway.Tests.Features.Outbound;

/// <summary>
/// Две реплики выполняют цикл диспетчера одновременно: каждая запись выбирается не более одного раза
/// в пределах цикла, поэтому продукт не получает два одинаковых результата из одного цикла.
/// </summary>
[NotInParallel]
public class OutboundResultDispatcherConcurrencyTests
{
    private const int Batch = 10;
    private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(10);

    [Test]
    public async Task Two_replicas_deliver_each_pending_result_once_per_cycle()
    {
        await using var fixture = new OutboundFixture();
        var createdAt = DateTimeOffset.UtcNow;

        for (var index = 0; index < Batch; index++)
        {
            await AddPendingAsync(fixture, $"msg-{index}", createdAt.AddMilliseconds(index));
        }

        var first = fixture.CreateDispatcher();
        var second = fixture.CreateDispatcher();

        await Task.WhenAll(first.DispatchPendingAsync(), second.DispatchPendingAsync());

        var delivered = await fixture.Results.WaitForCountAsync(Batch, DeliveryTimeout);

        await Assert.That(delivered.Count).IsEqualTo(Batch);
        await Assert.That(delivered.Select(result => result.MessageId).Distinct().Count()).IsEqualTo(Batch);
    }

    private static async Task AddPendingAsync(
        OutboundFixture fixture,
        string messageId,
        DateTimeOffset createdAt)
    {
        using var db = fixture.CreateDb();

        db.OutboundOperations.Add(new OutboundOperation
        {
            Id = Guid.NewGuid(),
            IntegrationId = OutboundFixture.IntegrationId,
            MessageId = messageId,
            ProductId = OutboundFixture.ProductId,
            ChannelName = OutboundFixture.ChannelName,
            Status = OutboundOperationStatus.Completed,
            Outcome = OutcomeNames.HttpResponse,
            HttpStatusCode = 200,
            AttemptCount = 1,
            CreatedAt = createdAt,
            CompletedAt = createdAt,
        });

        db.OutboundMessageLogs.Add(new OutboundMessageLog
        {
            Id = Guid.NewGuid(),
            IntegrationId = OutboundFixture.IntegrationId,
            MessageId = messageId,
            ProductId = OutboundFixture.ProductId,
            ChannelName = OutboundFixture.ChannelName,
            DeliveryAddress = OutboundFixture.ProductInbox,
            HttpMethod = "GET",
            RelativePath = "/api/contracts",
            RequestBody = [],
            ResponseBody = [],
            Outcome = OutcomeNames.HttpResponse,
            HttpStatusCode = 200,
            AttemptCount = 1,
            DeliveryStatus = OutboundDeliveryStatus.Pending,
            CreatedAt = createdAt,
        });

        await db.SaveChangesAsync();
    }
}
