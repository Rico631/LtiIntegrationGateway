using System.Net;
using LtiIntegrationGateway.Contracts.Messages.V1;
using LtiIntegrationGateway.Features.Outbound;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities;
using LtiIntegrationGateway.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace LtiIntegrationGateway.Tests.Features.Outbound;

public class OutboundResultDispatcherTests
{
    private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(5);

    private static ProcessOutboundRequestCommand Command(OutboundRequest request) =>
        new(request, OutboundFixture.ProductInbox, OutboundFixture.ProductInbox);

    [Test]
    public async Task Pending_result_is_sent_again_and_marked_as_sent()
    {
        await using var fixture = new OutboundFixture();
        await fixture.CreateActiveChannelAsync();
        await fixture.RegisterProductAsync();

        fixture.Http.EnqueueResponse(HttpStatusCode.OK, "{\"ok\":true}");

        var request = OutboundFixture.CreateRequest();
        request.CorrelationId = "corr-1";

        using (var db = fixture.CreateDb())
        {
            await fixture.CreateProcessor(db).HandleAsync(Command(request));
        }

        await fixture.Results.WaitForSingleAsync(DeliveryTimeout);

        // Первая отправка не подтвердилась: запись осталась в ожидании (ADR §8.9, шаг 4).
        await MarkPendingAsync(fixture);

        var bus = new RecordingBus();

        await fixture.CreateDispatcher(bus).DispatchPendingAsync();

        await Assert.That(bus.Sent.Count).IsEqualTo(1);
        await Assert.That(bus.Sent[0].Address).IsEqualTo(OutboundFixture.ProductInbox);

        var restored = bus.Sent[0].Message as OutboundResult;

        await Assert.That(restored).IsNotNull();
        await Assert.That(restored!.MessageId).IsEqualTo("msg-1");
        await Assert.That(restored.ResponseBody.ToStringUtf8()).IsEqualTo("{\"ok\":true}");
        await Assert.That(restored.ContentType).IsEqualTo("application/json");
        await Assert.That(restored.HttpStatusCode).IsEqualTo(200);
        await Assert.That(restored.Outcome).IsEqualTo(Outcome.HttpResponse);
        await Assert.That(restored.CorrelationId).IsEqualTo("corr-1");

        using var verifyDb = fixture.CreateDb();

        await Assert.That((await verifyDb.OutboundMessageLogs.SingleAsync()).DeliveryStatus)
            .IsEqualTo(OutboundDeliveryStatus.Sent);
    }

    [Test]
    public async Task Sent_result_is_not_sent_again()
    {
        await using var fixture = new OutboundFixture();
        await fixture.CreateActiveChannelAsync();
        await fixture.RegisterProductAsync();

        fixture.Http.EnqueueResponse(HttpStatusCode.OK);

        using (var db = fixture.CreateDb())
        {
            await fixture.CreateProcessor(db).HandleAsync(Command(OutboundFixture.CreateRequest()));
        }

        await fixture.Results.WaitForSingleAsync(DeliveryTimeout);

        var bus = new RecordingBus();

        await fixture.CreateDispatcher(bus).DispatchPendingAsync();

        await Assert.That(bus.Sent.Count).IsEqualTo(0);

        using var verifyDb = fixture.CreateDb();

        await Assert.That((await verifyDb.OutboundMessageLogs.SingleAsync()).DeliveryStatus)
            .IsEqualTo(OutboundDeliveryStatus.Sent);
    }

    [Test]
    public async Task Out_of_range_options_are_rejected()
    {
        await Assert.That(Rejects(new OutboundDeliveryOptions { IntervalSeconds = 0 })).IsTrue();
        await Assert.That(Rejects(new OutboundDeliveryOptions { BatchSize = 0 })).IsTrue();
        await Assert.That(Rejects(new OutboundDeliveryOptions { BatchSize = 501 })).IsTrue();
        await Assert.That(Rejects(new OutboundDeliveryOptions())).IsFalse();
    }

    private static bool Rejects(OutboundDeliveryOptions options)
    {
        try
        {
            options.Validate();

            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    [Test]
    public async Task Failed_send_keeps_entry_pending_and_does_not_stop_the_batch()
    {
        await using var fixture = new OutboundFixture();
        await fixture.CreateActiveChannelAsync();
        await fixture.RegisterProductAsync();

        fixture.Http.EnqueueResponse(HttpStatusCode.OK, "{\"id\":1}");
        fixture.Http.EnqueueResponse(HttpStatusCode.OK, "{\"id\":2}");

        using (var db = fixture.CreateDb())
        {
            var processor = fixture.CreateProcessor(db);

            await processor.HandleAsync(Command(OutboundFixture.CreateRequest("msg-1")));
            await processor.HandleAsync(Command(OutboundFixture.CreateRequest("msg-2")));
        }

        await fixture.Results.WaitForCountAsync(2, DeliveryTimeout);
        await MarkPendingAsync(fixture);

        var bus = new RecordingBus((_, message) => message is OutboundResult { MessageId: "msg-1" });

        await fixture.CreateDispatcher(bus).DispatchPendingAsync();

        await Assert.That(bus.Sent.Count).IsEqualTo(1);
        await Assert.That((bus.Sent[0].Message as OutboundResult)?.MessageId).IsEqualTo("msg-2");

        using var verifyDb = fixture.CreateDb();

        var logs = await verifyDb.OutboundMessageLogs.OrderBy(log => log.MessageId).ToListAsync();

        await Assert.That(logs.Count).IsEqualTo(2);
        await Assert.That(logs[0].DeliveryStatus).IsEqualTo(OutboundDeliveryStatus.Pending);
        await Assert.That(logs[1].DeliveryStatus).IsEqualTo(OutboundDeliveryStatus.Sent);
    }

    [Test]
    public async Task Disabled_dispatcher_does_not_start_and_leaves_entries_pending()
    {
        await using var fixture = new OutboundFixture();
        await fixture.CreateActiveChannelAsync();
        await fixture.RegisterProductAsync();

        fixture.Http.EnqueueResponse(HttpStatusCode.OK);

        using (var db = fixture.CreateDb())
        {
            await fixture.CreateProcessor(db).HandleAsync(Command(OutboundFixture.CreateRequest()));
        }

        await fixture.Results.WaitForSingleAsync(DeliveryTimeout);
        await MarkPendingAsync(fixture);

        var bus = new RecordingBus();

        using var dispatcher = fixture.CreateDispatcher(bus, options => options.Enabled = false);

        await dispatcher.StartAsync(CancellationToken.None);
        await dispatcher.StopAsync(CancellationToken.None);

        await Assert.That(dispatcher.ExecuteTask).IsNotNull();
        await Assert.That(dispatcher.ExecuteTask!.IsCompleted).IsTrue();
        await Assert.That(bus.Sent.Count).IsEqualTo(0);

        using var verifyDb = fixture.CreateDb();

        await Assert.That((await verifyDb.OutboundMessageLogs.SingleAsync()).DeliveryStatus)
            .IsEqualTo(OutboundDeliveryStatus.Pending);
    }

    [Test]
    public async Task Records_without_address_do_not_starve_deliverable_ones()
    {
        await using var fixture = new OutboundFixture();
        await fixture.CreateActiveChannelAsync();
        await fixture.RegisterProductAsync();

        var createdAt = DateTimeOffset.UtcNow;

        // Недоставляемые записи старше доставляемой: без фильтра они бы заняли всю пачку.
        await AddPendingAsync(fixture, "msg-no-address-1", string.Empty, createdAt);
        await AddPendingAsync(fixture, "msg-no-address-2", string.Empty, createdAt.AddMilliseconds(1));
        await AddPendingAsync(fixture, "msg-deliverable", OutboundFixture.ProductInbox, createdAt.AddMilliseconds(2));

        var bus = new RecordingBus();

        await fixture
            .CreateDispatcher(bus, options => options.BatchSize = 2)
            .DispatchPendingAsync();

        await Assert.That(bus.Sent.Count).IsEqualTo(1);
        await Assert.That((bus.Sent[0].Message as OutboundResult)?.MessageId).IsEqualTo("msg-deliverable");

        using var verifyDb = fixture.CreateDb();

        var logs = await verifyDb.OutboundMessageLogs.ToListAsync();

        await Assert.That(logs.Count).IsEqualTo(3);
        await Assert.That(logs.Single(log => log.MessageId == "msg-deliverable").DeliveryStatus)
            .IsEqualTo(OutboundDeliveryStatus.Sent);
        await Assert.That(logs.Single(log => log.MessageId == "msg-no-address-1").DeliveryStatus)
            .IsEqualTo(OutboundDeliveryStatus.Pending);
        await Assert.That(logs.Single(log => log.MessageId == "msg-no-address-2").DeliveryStatus)
            .IsEqualTo(OutboundDeliveryStatus.Pending);
    }

    [Test]
    public async Task Records_without_operation_or_address_are_skipped()
    {
        await using var fixture = new OutboundFixture();
        await fixture.CreateActiveChannelAsync();
        await fixture.RegisterProductAsync();

        var createdAt = DateTimeOffset.UtcNow;

        await AddPendingAsync(fixture, "msg-without-operation", OutboundFixture.ProductInbox, createdAt, withOperation: false);
        await AddPendingAsync(fixture, "msg-without-address", string.Empty, createdAt.AddMilliseconds(1));

        var bus = new RecordingBus();

        await fixture.CreateDispatcher(bus).DispatchPendingAsync();

        await Assert.That(bus.Sent.Count).IsEqualTo(0);

        using var verifyDb = fixture.CreateDb();

        var logs = await verifyDb.OutboundMessageLogs.ToListAsync();

        await Assert.That(logs.Count).IsEqualTo(2);
        await Assert.That(logs.All(log => log.DeliveryStatus == OutboundDeliveryStatus.Pending)).IsTrue();
    }

    [Test]
    public async Task Foreign_request_does_not_mark_pending_record_of_owner()
    {
        await using var fixture = new OutboundFixture();
        await fixture.CreateActiveChannelAsync();
        await fixture.RegisterProductAsync();

        // Журнал владельца: операция завершена, результат ещё не доставлен.
        await AddPendingAsync(fixture, "msg-owned", OutboundFixture.ProductInbox, DateTimeOffset.UtcNow);

        using (var db = fixture.CreateDb())
        {
            await Assert.That(await db.OutboundMessageLogs.CountAsync()).IsEqualTo(1);
        }

        var foreign = OutboundFixture.CreateRequest("msg-owned");
        foreign.ProductId = "another-product";

        using (var db = fixture.CreateDb())
        {
            var result = await fixture.CreateProcessor(db).HandleAsync(
                new ProcessOutboundRequestCommand(
                    foreign,
                    OutboundFixture.ProductInbox,
                    OutboundFixture.ProductInbox));

            await Assert.That(result.Completed).IsFalse();
        }

        // Чужой отказ не доставляется и не помечает запись владельца как доставленную.
        await Assert.That(fixture.Results.Results.Count).IsEqualTo(0);

        using (var db = fixture.CreateDb())
        {
            var log = await db.OutboundMessageLogs.SingleAsync();

            await Assert.That(log.ProductId).IsEqualTo(OutboundFixture.ProductId);
            await Assert.That(log.DeliveryStatus).IsEqualTo(OutboundDeliveryStatus.Pending);
        }

        // Следующий цикл диспетчера доставляет сохранённый результат владельцу.
        var bus = new RecordingBus();

        await fixture.CreateDispatcher(bus).DispatchPendingAsync();

        await Assert.That(bus.Sent.Count).IsEqualTo(1);
        await Assert.That(bus.Sent[0].Address).IsEqualTo(OutboundFixture.ProductInbox);

        var restored = bus.Sent[0].Message as OutboundResult;

        await Assert.That(restored).IsNotNull();
        await Assert.That(restored!.MessageId).IsEqualTo("msg-owned");
        await Assert.That(restored.Outcome).IsEqualTo(Outcome.HttpResponse);

        using var verifyDb = fixture.CreateDb();

        await Assert.That((await verifyDb.OutboundMessageLogs.SingleAsync()).DeliveryStatus)
            .IsEqualTo(OutboundDeliveryStatus.Sent);
    }

    /// <summary>Создаёт запись журнала со статусом «ожидает отправки» и, если нужно, операцию к ней.</summary>
    private static async Task AddPendingAsync(
        OutboundFixture fixture,
        string messageId,
        string deliveryAddress,
        DateTimeOffset createdAt,
        bool withOperation = true)
    {
        using var db = fixture.CreateDb();

        if (withOperation)
        {
            db.OutboundOperations.Add(new OutboundOperation
            {
                Id = Guid.NewGuid(),
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
        }

        db.OutboundMessageLogs.Add(new OutboundMessageLog
        {
            Id = Guid.NewGuid(),
            MessageId = messageId,
            ProductId = OutboundFixture.ProductId,
            ChannelName = OutboundFixture.ChannelName,
            DeliveryAddress = deliveryAddress,
            HttpMethod = "GET",
            RelativePath = "/api/contracts",
            RequestBody = [],
            ResponseBody = "{\"ok\":true}"u8.ToArray(),
            ContentType = "application/json",
            HttpStatusCode = 200,
            Outcome = OutcomeNames.HttpResponse,
            ChannelRevisionNumber = 1,
            AttemptCount = 1,
            DeliveryStatus = OutboundDeliveryStatus.Pending,
            CreatedAt = createdAt,
        });

        await db.SaveChangesAsync();
    }

    /// <summary>Переводит все записи журнала в «ожидает отправки», имитируя неподтверждённую отправку.</summary>
    private static async Task MarkPendingAsync(OutboundFixture fixture)
    {
        using var db = fixture.CreateDb();

        foreach (var log in await db.OutboundMessageLogs.ToListAsync())
        {
            log.DeliveryStatus = OutboundDeliveryStatus.Pending;
        }

        await db.SaveChangesAsync();
    }
}
