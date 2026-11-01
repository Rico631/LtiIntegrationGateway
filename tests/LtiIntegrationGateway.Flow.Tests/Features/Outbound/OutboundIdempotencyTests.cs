using System.Net;
using LtiIntegrationGateway.Contracts.Messages.V1;
using LtiIntegrationGateway.Features.Outbound;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Outbound;
using LtiIntegrationGateway.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace LtiIntegrationGateway.Tests.Features.Outbound;

public class OutboundIdempotencyTests
{
    private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(5);

    private static ProcessOutboundRequestCommand Command(OutboundRequest request) =>
        new(request, OutboundFixture.ProductInbox, OutboundFixture.ProductInbox);

    [Test]
    public async Task Repeated_message_id_does_not_repeat_external_call()
    {
        await using var fixture = new OutboundFixture();
        await fixture.CreateActiveChannelAsync();
        await fixture.RegisterProductAsync();

        fixture.Http.EnqueueResponse(HttpStatusCode.OK, "{\"ok\":true}");

        var request = OutboundFixture.CreateRequest();

        using (var db = fixture.CreateDb())
        {
            await fixture.CreateProcessor(db).HandleAsync(Command(request));
        }

        await fixture.Results.WaitForSingleAsync(DeliveryTimeout);

        using (var db = fixture.CreateDb())
        {
            await fixture.CreateProcessor(db).HandleAsync(Command(request));
        }

        var results = await fixture.Results.WaitForCountAsync(2, DeliveryTimeout);

        await Assert.That(fixture.Http.CallCount).IsEqualTo(1);
        await Assert.That(results[1].MessageId).IsEqualTo("msg-1");
        await Assert.That(results[1].Outcome).IsEqualTo(Outcome.HttpResponse);
    }

    [Test]
    public async Task In_progress_operation_blocks_duplicate_call()
    {
        await using var fixture = new OutboundFixture();
        await fixture.CreateActiveChannelAsync();
        await fixture.RegisterProductAsync();

        var request = OutboundFixture.CreateRequest("msg-running");

        using (var db = fixture.CreateDb())
        {
            db.OutboundOperations.Add(new OutboundOperation
            {
                Id = Guid.NewGuid(),
                MessageId = request.MessageId,
                ProductId = request.ProductId,
                ChannelName = request.ChannelName,
                Status = OutboundOperationStatus.InProgress,
                AttemptCount = 0,
                CreatedAt = DateTimeOffset.UtcNow,
            });

            await db.SaveChangesAsync();
        }

        using (var db = fixture.CreateDb())
        {
            await fixture.CreateProcessor(db).HandleAsync(Command(request));
        }

        await Assert.That(fixture.Http.CallCount).IsEqualTo(0);
        await Assert.That(fixture.Results.Results.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Message_id_of_another_product_is_refused()
    {
        await using var fixture = new OutboundFixture();
        await fixture.CreateActiveChannelAsync();
        await fixture.RegisterProductAsync();

        var request = OutboundFixture.CreateRequest("msg-foreign");

        using (var db = fixture.CreateDb())
        {
            db.OutboundOperations.Add(new OutboundOperation
            {
                Id = Guid.NewGuid(),
                MessageId = request.MessageId,
                ProductId = "another-product",
                ChannelName = request.ChannelName,
                Status = OutboundOperationStatus.Completed,
                Outcome = OutcomeNames.HttpResponse,
                AttemptCount = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow,
            });

            await db.SaveChangesAsync();
        }

        using (var db = fixture.CreateDb())
        {
            await fixture.CreateProcessor(db).HandleAsync(Command(request));
        }

        await Assert.That(fixture.Http.CallCount).IsEqualTo(0);
        await Assert.That(fixture.Results.Results.Count).IsEqualTo(0);

        // Состояние чужой операции и журнал не меняются: сохранённый результат не отдаётся,
        // а запись отказа не создаётся.
        using var verifyDb = fixture.CreateDb();
        var operation = await verifyDb.OutboundOperations.SingleAsync();

        await Assert.That(operation.ProductId).IsEqualTo("another-product");
        await Assert.That(operation.Status).IsEqualTo(OutboundOperationStatus.Completed);
        await Assert.That(operation.Outcome).IsEqualTo(OutcomeNames.HttpResponse);
        await Assert.That(await verifyDb.OutboundMessageLogs.CountAsync()).IsEqualTo(0);
    }
}