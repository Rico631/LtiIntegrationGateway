using System.Diagnostics;
using Google.Protobuf;
using LtiIntegrationGateway.Contracts.Messages.V1;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Inbound;
using LtiIntegrationGateway.Tests.TestSupport;

namespace LtiIntegrationGateway.Tests.Features.Inbound;

/// <summary>Синхронный inbound: ответ продукта, ошибки, timeout, отмена и журнал.</summary>
public class SyncInboundTests
{
    [Test]
    public async Task Sync_success_returns_product_status_body_and_allowed_headers()
    {
        await using var fixture = new InboundFixture();
        fixture.Received.ReplyFactory = message => new InboundResponse
        {
            MessageId = message.MessageId,
            HttpStatusCode = 201,
            ContentType = "application/json",
            Body = ByteString.CopyFromUtf8("{\"accepted\":true}"),
            Headers =
            {
                new InboundResponseHeader { Name = "Location", Value = "/result/1" },
                new InboundResponseHeader { Name = "Retry-After", Value = "5" },
            },
            ErrorCode = "product_accepted",
        };

        var response = await fixture.InvokeWithResponseAsync(
            fixture.Route(responseMode: InboundResponseMode.Sync),
            body: null);


        await Assert.That(response.StatusCode).IsEqualTo(201);
        await Assert.That(response.Body).IsEqualTo("{\"accepted\":true}");
        await Assert.That(response.Headers["Location"].Single()).IsEqualTo("/result/1");
        await Assert.That(response.Headers["Retry-After"].Single()).IsEqualTo("5");

        var messages = await fixture.Received.WaitForCountAsync(1, TimeSpan.FromSeconds(2));
        await Assert.That(messages[0].ReplyExpected).IsTrue();

        var log = (await fixture.LogsAsync()).Single();
        await Assert.That(log.DeliveryStatus).IsEqualTo(InboundDeliveryStatus.Replied);
        await Assert.That(log.ResponseStatusCode).IsEqualTo(201);
        await Assert.That(log.ResponseContentType).IsEqualTo("application/json");
        await Assert.That(log.ResponseBody).IsEquivalentTo(ByteString.CopyFromUtf8("{\"accepted\":true}").ToByteArray());
        await Assert.That(log.ResponseHeaders).Contains("Location");
        await Assert.That(log.ErrorCode).IsEqualTo("product_accepted");
    }

    [Test]
    public async Task Product_4xx_is_returned_without_translation()
    {
        await using var fixture = new InboundFixture();
        fixture.Received.ReplyFactory = message => new InboundResponse
        {
            MessageId = message.MessageId,
            HttpStatusCode = 422,
            ContentType = "application/json",
            Body = ByteString.CopyFromUtf8("{\"error\":\"invalid\"}"),
        };

        var response = await fixture.InvokeWithResponseAsync(
            fixture.Route(responseMode: InboundResponseMode.Sync),
            body: null);

        await Assert.That(response.StatusCode).IsEqualTo(422);
        await Assert.That(response.Body).Contains("invalid");
    }

    [Test]
    public async Task Missing_reply_returns_gateway_timeout()
    {
        await using var fixture = new InboundFixture();
        var stopwatch = Stopwatch.StartNew();

        var response = await fixture.InvokeWithResponseAsync(
            fixture.Route(responseMode: InboundResponseMode.Sync, inboundResponseTimeoutMs: 100),
            body: null);

        await Assert.That(response.StatusCode).IsEqualTo(504);
        await Assert.That(stopwatch.Elapsed).IsLessThan(TimeSpan.FromSeconds(5));

        var log = (await fixture.LogsAsync()).Single();
        await Assert.That(log.DeliveryStatus).IsEqualTo(InboundDeliveryStatus.TimedOut);
        await Assert.That(log.ErrorCode).IsEqualTo("inbound_timeout");
        await Assert.That(log.ResponseStatusCode).IsNull();
    }

    [Test]
    public async Task Message_id_mismatch_is_rejected_fail_closed()
    {
        await using var fixture = new InboundFixture();
        fixture.Received.ReplyFactory = _ => new InboundResponse
        {
            MessageId = "another-message",
            HttpStatusCode = 200,
            ContentType = "text/plain",
            Body = ByteString.CopyFromUtf8("secret"),
        };

        var response = await fixture.InvokeWithResponseAsync(
            fixture.Route(responseMode: InboundResponseMode.Sync),
            body: null);

        await Assert.That(response.StatusCode).IsEqualTo(502);
        await Assert.That(response.Body).DoesNotContain("secret");

        var log = (await fixture.LogsAsync()).Single();
        await Assert.That(log.DeliveryStatus).IsEqualTo(InboundDeliveryStatus.Failed);
        await Assert.That(log.ErrorCode).IsEqualTo("invalid_inbound_response");
    }

    [Test]
    public async Task Header_outside_allowlist_is_rejected_fail_closed()
    {
        await using var fixture = new InboundFixture();
        fixture.Received.ReplyFactory = message => new InboundResponse
        {
            MessageId = message.MessageId,
            HttpStatusCode = 200,
            Headers = { new InboundResponseHeader { Name = "X-Secret", Value = "value" } },
        };

        var response = await fixture.InvokeWithResponseAsync(
            fixture.Route(responseMode: InboundResponseMode.Sync),
            body: null);

        await Assert.That(response.StatusCode).IsEqualTo(502);
        await Assert.That((await fixture.LogsAsync()).Single().ErrorCode).IsEqualTo("invalid_inbound_response");
    }

    [Test]
    public async Task Redirect_status_is_rejected_fail_closed()
    {
        await using var fixture = new InboundFixture();
        fixture.Received.ReplyFactory = message => new InboundResponse
        {
            MessageId = message.MessageId,
            HttpStatusCode = 302,
            Headers = { new InboundResponseHeader { Name = "Location", Value = "/other" } },
        };

        var response = await fixture.InvokeWithResponseAsync(
            fixture.Route(responseMode: InboundResponseMode.Sync),
            body: null);

        await Assert.That(response.StatusCode).IsEqualTo(502);
    }

    [Test]
    public async Task Client_cancellation_marks_log_without_writing_a_response()
    {
        await using var fixture = new InboundFixture();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var response = await fixture.InvokeWithResponseAsync(
            fixture.Route(responseMode: InboundResponseMode.Sync, inboundResponseTimeoutMs: 5000),
            body: null,
            requestAborted: cancellation.Token);

        await Assert.That(response.Body).IsEqualTo(string.Empty);

        var log = (await fixture.LogsAsync()).Single();
        await Assert.That(log.DeliveryStatus).IsEqualTo(InboundDeliveryStatus.Cancelled);
        await Assert.That(log.ErrorCode).IsEqualTo("cancelled");
    }

    [Test]
    public async Task Late_product_reply_does_not_change_timeout_result_or_crash_the_bus()
    {
        await using var fixture = new InboundFixture();
        fixture.Received.ReplyDelay = TimeSpan.FromMilliseconds(300);
        fixture.Received.ReplyFactory = message => new InboundResponse
        {
            MessageId = message.MessageId,
            HttpStatusCode = 200,
            ContentType = "text/plain",
            Body = ByteString.CopyFromUtf8("late"),
        };

        var response = await fixture.InvokeWithResponseAsync(
            fixture.Route(responseMode: InboundResponseMode.Sync, inboundResponseTimeoutMs: 50),
            body: null);

        await Assert.That(response.StatusCode).IsEqualTo(504);
        await Assert.That((await fixture.LogsAsync()).Single().DeliveryStatus)
            .IsEqualTo(InboundDeliveryStatus.TimedOut);

        // Даём продукту отправить поздний ответ; шлюз уже не ждёт его.
        await Task.Delay(400);
        await Assert.That((await fixture.LogsAsync()).Single().DeliveryStatus)
            .IsEqualTo(InboundDeliveryStatus.TimedOut);
    }

    [Test]
    public async Task Product_5xx_is_returned_without_translation()
    {
        await using var fixture = new InboundFixture();
        fixture.Received.ReplyFactory = message => new InboundResponse
        {
            MessageId = message.MessageId,
            HttpStatusCode = 503,
            ContentType = "text/plain",
            Body = ByteString.CopyFromUtf8("temporarily unavailable"),
        };

        var response = await fixture.InvokeWithResponseAsync(
            fixture.Route(responseMode: InboundResponseMode.Sync),
            body: null);

        await Assert.That(response.StatusCode).IsEqualTo(503);
        await Assert.That(response.Body).IsEqualTo("temporarily unavailable");
    }

}
