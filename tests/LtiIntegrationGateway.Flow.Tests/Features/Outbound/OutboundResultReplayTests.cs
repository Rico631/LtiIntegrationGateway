using System.Net;
using LtiIntegrationGateway.Contracts.Messages.V1;
using LtiIntegrationGateway.Features.Outbound;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Outbound;
using LtiIntegrationGateway.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace LtiIntegrationGateway.Tests.Features.Outbound;

public class OutboundResultReplayTests
{
    private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(5);

    private static ProcessOutboundRequestCommand Command(OutboundRequest request, string returnAddress) =>
        new(request, OutboundFixture.ProductInbox, returnAddress);

    [Test]
    public async Task Replay_returns_result_identical_to_first()
    {
        await using var fixture = new OutboundFixture();
        await fixture.CreateActiveChannelAsync();
        await fixture.RegisterProductAsync();

        fixture.Http.EnqueueResponse(HttpStatusCode.Created, "{\"id\":7}");

        var first = OutboundFixture.CreateRequest(
            method: "POST",
            relativePath: "/api/contracts",
            idempotencyKey: "idem-1",
            body: "{\"name\":\"x\"}"u8.ToArray(),
            contentType: "application/json");

        first.CorrelationId = "corr-first";

        using (var db = fixture.CreateDb())
        {
            await fixture.CreateProcessor(db).HandleAsync(Command(first, OutboundFixture.ProductInbox));
        }

        var deliveredFirst = await fixture.Results.WaitForSingleAsync(DeliveryTimeout);

        // Повтор приходит с другим correlation_id: результат обязан остаться прежним.
        var second = OutboundFixture.CreateRequest(
            method: "POST",
            relativePath: "/api/contracts",
            idempotencyKey: "idem-1",
            body: "{\"name\":\"x\"}"u8.ToArray(),
            contentType: "application/json");

        second.CorrelationId = "corr-second";

        using (var db = fixture.CreateDb())
        {
            await fixture.CreateProcessor(db).HandleAsync(Command(second, OutboundFixture.ProductInbox));
        }

        var delivered = await fixture.Results.WaitForCountAsync(2, DeliveryTimeout);
        var replayed = delivered[1];

        await Assert.That(fixture.Http.CallCount).IsEqualTo(1);
        await Assert.That(ContractJson.Serialize(replayed)).IsEqualTo(ContractJson.Serialize(deliveredFirst));
        await Assert.That(replayed.ResponseBody.ToStringUtf8()).IsEqualTo("{\"id\":7}");
        await Assert.That(replayed.ContentType).IsEqualTo("application/json");
        await Assert.That(replayed.HttpStatusCode).IsEqualTo(201);
        await Assert.That(replayed.Outcome).IsEqualTo(Outcome.HttpResponse);
        await Assert.That(replayed.AttemptCount).IsEqualTo(deliveredFirst.AttemptCount);
        await Assert.That(replayed.CorrelationId).IsEqualTo("corr-first");
    }

    [Test]
    public async Task Replay_is_sent_to_computed_inbox_from_current_message()
    {
        await using var fixture = new OutboundFixture();
        await fixture.CreateActiveChannelAsync();
        await fixture.RegisterProductAsync();

        fixture.Http.EnqueueResponse(HttpStatusCode.OK);

        var bus = new RecordingBus();

        using (var db = fixture.CreateDb())
        {
            await fixture.CreateProcessor(db, bus).HandleAsync(
                Command(OutboundFixture.CreateRequest(), OutboundFixture.ProductInbox));
        }

        using (var db = fixture.CreateDb())
        {
            await fixture.CreateProcessor(db, bus).HandleAsync(
                Command(OutboundFixture.CreateRequest(), OutboundFixture.ProductInbox));
        }

        await Assert.That(bus.Sent.Count).IsEqualTo(2);
        await Assert.That(bus.Sent[0].Address).IsEqualTo(OutboundFixture.ProductInbox);
        await Assert.That(bus.Sent[1].Address).IsEqualTo(OutboundFixture.ProductInbox);
    }

    [Test]
    public async Task Replay_without_journal_entry_does_not_fail()
    {
        await using var fixture = new OutboundFixture();
        await fixture.CreateActiveChannelAsync();
        await fixture.RegisterProductAsync();

        var request = OutboundFixture.CreateRequest("msg-without-log");
        request.CorrelationId = "corr-stored";

        using (var db = fixture.CreateDb())
        {
            db.OutboundOperations.Add(new OutboundOperation
            {
                Id = Guid.NewGuid(),
                MessageId = request.MessageId,
                ProductId = request.ProductId,
                ChannelName = request.ChannelName,
                Status = OutboundOperationStatus.Completed,
                Outcome = OutcomeNames.HttpResponse,
                HttpStatusCode = 200,
                AttemptCount = 1,
                CorrelationId = "corr-stored",
                CreatedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow,
            });

            await db.SaveChangesAsync();
        }

        var bus = new RecordingBus();

        using (var db = fixture.CreateDb())
        {
            var result = await fixture.CreateProcessor(db, bus).HandleAsync(
                Command(request, OutboundFixture.ProductInbox));

            await Assert.That(result.Completed).IsTrue();
        }

        await Assert.That(fixture.Http.CallCount).IsEqualTo(0);
        await Assert.That(bus.Sent.Count).IsEqualTo(1);

        var restored = bus.Sent[0].Message as OutboundResult;

        await Assert.That(restored).IsNotNull();
        await Assert.That(restored!.MessageId).IsEqualTo("msg-without-log");
        await Assert.That(restored.CorrelationId).IsEqualTo("corr-stored");
        await Assert.That(restored.HttpStatusCode).IsEqualTo(200);
        await Assert.That(restored.Outcome).IsEqualTo(Outcome.HttpResponse);
    }

    [Test]
    public async Task Replay_restores_transport_error()
    {
        await using var fixture = new OutboundFixture();
        await fixture.CreateActiveChannelAsync(
            retryPolicy: new RetryPolicy([RetryRule.HttpStatusIn([503], [1])]));
        await fixture.RegisterProductAsync();

        fixture.Http.EnqueueException(new HttpRequestException("соединение сброшено"));

        var request = OutboundFixture.CreateRequest("msg-transport");

        using (var db = fixture.CreateDb())
        {
            await fixture.CreateProcessor(db).HandleAsync(Command(request, OutboundFixture.ProductInbox));
        }

        var deliveredFirst = await fixture.Results.WaitForSingleAsync(DeliveryTimeout);

        using (var db = fixture.CreateDb())
        {
            await fixture.CreateProcessor(db).HandleAsync(Command(request, OutboundFixture.ProductInbox));
        }

        var delivered = await fixture.Results.WaitForCountAsync(2, DeliveryTimeout);
        var replayed = delivered[1];

        await Assert.That(fixture.Http.CallCount).IsEqualTo(1);
        await Assert.That(deliveredFirst.Outcome).IsEqualTo(Outcome.TransportError);
        await Assert.That(replayed.Outcome).IsEqualTo(Outcome.TransportError);
        await Assert.That(replayed.ErrorCode).IsEqualTo(OutboundErrorCodes.ConnectionFailed);
        await Assert.That(replayed.ErrorMessage).IsEqualTo(deliveredFirst.ErrorMessage);
        await Assert.That(replayed.ErrorMessage).IsNotEmpty();
        await Assert.That(replayed.AttemptCount).IsEqualTo(1);
        await Assert.That(ContractJson.Serialize(replayed)).IsEqualTo(ContractJson.Serialize(deliveredFirst));
    }

    [Test]
    public async Task Replay_restores_refused_result()
    {
        await using var fixture = new OutboundFixture();
        await fixture.CreateActiveChannelAsync();
        await fixture.RegisterProductAsync();

        // Структурный отказ по пути: повтор не должен заново проходить проверки канала.
        var request = OutboundFixture.CreateRequest(
            "msg-refused",
            relativePath: "https://evil.example/api/admin");

        using (var db = fixture.CreateDb())
        {
            await fixture.CreateProcessor(db).HandleAsync(Command(request, OutboundFixture.ProductInbox));
        }

        var deliveredFirst = await fixture.Results.WaitForSingleAsync(DeliveryTimeout);

        using (var db = fixture.CreateDb())
        {
            await fixture.CreateProcessor(db).HandleAsync(Command(request, OutboundFixture.ProductInbox));
        }

        var delivered = await fixture.Results.WaitForCountAsync(2, DeliveryTimeout);
        var replayed = delivered[1];

        await Assert.That(fixture.Http.CallCount).IsEqualTo(0);
        await Assert.That(deliveredFirst.Outcome).IsEqualTo(Outcome.GatewayRefused);
        await Assert.That(replayed.Outcome).IsEqualTo(Outcome.GatewayRefused);
        await Assert.That(replayed.ErrorCode).IsEqualTo(OutboundErrorCodes.PathNotAllowed);
        await Assert.That(replayed.ErrorMessage).IsEqualTo("путь недопустим");
        await Assert.That(replayed.AttemptCount).IsEqualTo(0);
        await Assert.That(ContractJson.Serialize(replayed)).IsEqualTo(ContractJson.Serialize(deliveredFirst));
    }

    [Test]
    public async Task Completed_operation_is_replayed_when_channel_is_deactivated()
    {
        await using var fixture = new OutboundFixture();
        await fixture.CreateActiveChannelAsync();
        await fixture.RegisterProductAsync();

        fixture.Http.EnqueueResponse(HttpStatusCode.OK, "{\"ok\":true}");

        var request = OutboundFixture.CreateRequest("msg-inactive-channel");

        using (var db = fixture.CreateDb())
        {
            await fixture.CreateProcessor(db).HandleAsync(Command(request, OutboundFixture.ProductInbox));
        }

        var deliveredFirst = await fixture.Results.WaitForSingleAsync(DeliveryTimeout);

        // У канала больше нет активной ревизии: повтор не должен требовать её для сохранённого результата.
        using (var db = fixture.CreateDb())
        {
            var revision = await db.ChannelRevisions.SingleAsync();
            revision.Status = ChannelRevisionStatus.Published;

            await db.SaveChangesAsync();
        }

        using (var db = fixture.CreateDb())
        {
            await fixture.CreateProcessor(db).HandleAsync(Command(request, OutboundFixture.ProductInbox));
        }

        var delivered = await fixture.Results.WaitForCountAsync(2, DeliveryTimeout);
        var replayed = delivered[1];

        await Assert.That(fixture.Http.CallCount).IsEqualTo(1);
        await Assert.That(replayed.Outcome).IsEqualTo(Outcome.HttpResponse);
        await Assert.That(ContractJson.Serialize(replayed)).IsEqualTo(ContractJson.Serialize(deliveredFirst));
    }

    [Test]
    public async Task Operation_stores_delivery_address_and_correlation_id()
    {
        await using var fixture = new OutboundFixture();
        await fixture.CreateActiveChannelAsync();
        await fixture.RegisterProductAsync();

        fixture.Http.EnqueueResponse(HttpStatusCode.OK);

        var request = OutboundFixture.CreateRequest();
        request.CorrelationId = "corr-journal";

        using (var db = fixture.CreateDb())
        {
            await fixture.CreateProcessor(db).HandleAsync(Command(request, OutboundFixture.ProductInbox));
        }

        await fixture.Results.WaitForSingleAsync(DeliveryTimeout);

        using var verifyDb = fixture.CreateDb();

        var log = await verifyDb.OutboundMessageLogs.SingleAsync();
        var operation = await verifyDb.OutboundOperations.SingleAsync();

        await Assert.That(log.DeliveryAddress).IsEqualTo(OutboundFixture.ProductInbox);
        await Assert.That(log.ChannelRevisionNumber).IsEqualTo(1);
        await Assert.That(log.CreatedAt).IsGreaterThan(DateTimeOffset.MinValue);
        await Assert.That(operation.CorrelationId).IsEqualTo("corr-journal");
    }
}
