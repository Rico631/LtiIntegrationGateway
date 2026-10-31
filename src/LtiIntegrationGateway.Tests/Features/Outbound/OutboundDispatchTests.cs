using System.Net;
using LtiIntegrationGateway.Contracts.Messages.V1;
using LtiIntegrationGateway.Features.Outbound;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities;
using LtiIntegrationGateway.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Rebus.Exceptions;

namespace LtiIntegrationGateway.Tests.Features.Outbound;

public class OutboundDispatchTests
{
    private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(5);

    private static ProcessOutboundRequestCommand Command(
        OutboundRequest? request = null,
        string senderAddress = OutboundFixture.ProductInbox,
        string returnAddress = OutboundFixture.ProductInbox) =>
        new(request ?? OutboundFixture.CreateRequest(), senderAddress, returnAddress);

    [Test]
    public async Task Valid_request_performs_single_http_call_and_delivers_result()
    {
        await using var fixture = new OutboundFixture();
        await fixture.CreateActiveChannelAsync();
        await fixture.RegisterProductAsync();

        fixture.Http.EnqueueResponse(HttpStatusCode.OK, "{\"ok\":true}");

        using var db = fixture.CreateDb();
        var result = await fixture.CreateProcessor(db).HandleAsync(Command());

        await Assert.That(result.Completed).IsTrue();
        await Assert.That(fixture.Http.CallCount).IsEqualTo(1);
        await Assert.That(fixture.Http.Requests[0].Method).IsEqualTo("GET");

        var delivered = await fixture.Results.WaitForSingleAsync(DeliveryTimeout);

        await Assert.That(delivered.Outcome).IsEqualTo(Outcome.HttpResponse);
        await Assert.That(delivered.HttpStatusCode).IsEqualTo(200);
        await Assert.That(delivered.ResponseBody.ToStringUtf8()).IsEqualTo("{\"ok\":true}");
    }

    [Test]
    public async Task Invalid_dto_is_rejected_by_handler_without_side_effects()
    {
        await using var fixture = new OutboundFixture();
        await fixture.CreateActiveChannelAsync();
        await fixture.RegisterProductAsync();

        using var db = fixture.CreateDb();
        var request = OutboundFixture.CreateRequest();
        request.MessageId = string.Empty;

        var handler = new OutboundRequestHandler(fixture.CreateProcessor(db));

        await Assert.ThrowsAsync<FailFastException>(() => handler.Handle(request));
        await Assert.That(fixture.Http.CallCount).IsEqualTo(0);
        await Assert.That(await db.OutboundOperations.CountAsync()).IsEqualTo(0);
        await Assert.That(await db.OutboundMessageLogs.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task Unknown_product_is_refused_without_http_call()
    {
        await using var fixture = new OutboundFixture();
        await fixture.CreateActiveChannelAsync();
        await fixture.RegisterProductAsync();

        var request = OutboundFixture.CreateRequest();
        request.ProductId = "unknown-product";

        using var db = fixture.CreateDb();
        await fixture.CreateProcessor(db).HandleAsync(Command(request));

        await Assert.That(fixture.Http.CallCount).IsEqualTo(0);
        await Assert.That(fixture.Results.Results.Count).IsEqualTo(0);

        using var verifyDb = fixture.CreateDb();
        var log = await verifyDb.OutboundMessageLogs.SingleAsync();

        await Assert.That(log.Outcome).IsEqualTo(OutcomeNames.GatewayRefused);
        await Assert.That(log.ErrorCode).IsEqualTo(OutboundErrorCodes.ProductNotAllowed);
        await Assert.That(log.DeliveryStatus).IsEqualTo(OutboundDeliveryStatus.Undeliverable);
        await Assert.That(log.DeliveryAddress).IsEmpty();
    }

    [Test]
    public async Task Sender_address_not_allowed_for_product_is_refused()
    {
        await using var fixture = new OutboundFixture();
        await fixture.CreateActiveChannelAsync();
        await fixture.RegisterProductAsync();

        using var db = fixture.CreateDb();
        await fixture.CreateProcessor(db).HandleAsync(Command(senderAddress: "another.inbox"));

        await Assert.That(fixture.Http.CallCount).IsEqualTo(0);
        await Assert.That(fixture.Results.Results.Count).IsEqualTo(0);

        using var verifyDb = fixture.CreateDb();
        var log = await verifyDb.OutboundMessageLogs.SingleAsync();

        await Assert.That(log.ErrorCode).IsEqualTo(OutboundErrorCodes.ProductNotAllowed);
        await Assert.That(log.DeliveryStatus).IsEqualTo(OutboundDeliveryStatus.Undeliverable);
    }

    [Test]
    public async Task Return_address_not_equal_to_computed_inbox_is_refused()
    {
        await using var fixture = new OutboundFixture();
        await fixture.CreateActiveChannelAsync();
        await fixture.RegisterProductAsync();

        var bus = new RecordingBus();

        using (var db = fixture.CreateDb())
        {
            var result = await fixture.CreateProcessor(db, bus).HandleAsync(
                Command(returnAddress: "another.inbox"));

            await Assert.That(result.Completed).IsFalse();
            await Assert.That(result.Problems[0].StartsWith(
                OutboundErrorCodes.ProductNotAllowed, StringComparison.Ordinal)).IsTrue();
        }

        await Assert.That(fixture.Http.CallCount).IsEqualTo(0);
        await Assert.That(bus.Sent.Count).IsEqualTo(0);

        using var verifyDb = fixture.CreateDb();
        var log = await verifyDb.OutboundMessageLogs.SingleAsync();

        await Assert.That(log.ErrorCode).IsEqualTo(OutboundErrorCodes.ProductNotAllowed);
        await Assert.That(log.DeliveryStatus).IsEqualTo(OutboundDeliveryStatus.Undeliverable);
    }

    [Test]
    public async Task Channel_not_allowed_for_product_is_refused()
    {
        await using var fixture = new OutboundFixture();
        await fixture.CreateActiveChannelAsync();

        using (var db = fixture.CreateDb())
        {
            await new LtiIntegrationGateway.Features.ProductRoutes.RegisterProductRouteHandler(db)
                .HandleAsync(new LtiIntegrationGateway.Features.ProductRoutes.RegisterProductRouteCommand(
                    OutboundFixture.ProductId,
                    ["bank-b"],
                    [OutboundFixture.ProductInbox]));
        }

        using var processingDb = fixture.CreateDb();
        await fixture.CreateProcessor(processingDb).HandleAsync(Command());

        await Assert.That(fixture.Http.CallCount).IsEqualTo(0);
        await Assert.That(fixture.Results.Results.Count).IsEqualTo(0);

        using var verifyDb = fixture.CreateDb();
        var log = await verifyDb.OutboundMessageLogs.SingleAsync();

        await Assert.That(log.ErrorCode).IsEqualTo(OutboundErrorCodes.ChannelNotAllowed);
        await Assert.That(log.DeliveryStatus).IsEqualTo(OutboundDeliveryStatus.Undeliverable);
    }

    [Test]
    public async Task Channel_without_active_revision_is_refused()
    {
        await using var fixture = new OutboundFixture();
        fixture.Secrets.With("Secrets:BankA:Token", "token");

        using (var db = fixture.CreateDb())
        {
            var configuration = new LtiIntegrationGateway.Features.Channels.ChannelConfiguration(
                "https://bank-a.example",
                ChannelAuthType.None,
                null,
                null,
                null,
                5000,
                30000,
                new RetryPolicy([RetryRule.TransportError([1])]),
                ["GET"],
                ["/api/contracts"]);

            await new LtiIntegrationGateway.Features.Channels.CreateChannelHandler(db)
                .HandleAsync(new LtiIntegrationGateway.Features.Channels.CreateChannelCommand(
                    OutboundFixture.ChannelName,
                    configuration));
        }

        await fixture.RegisterProductAsync();

        using var processingDb = fixture.CreateDb();
        await fixture.CreateProcessor(processingDb).HandleAsync(Command());

        await Assert.That(fixture.Http.CallCount).IsEqualTo(0);

        var delivered = await fixture.Results.WaitForSingleAsync(DeliveryTimeout);

        await Assert.That(delivered.ErrorCode).IsEqualTo(OutboundErrorCodes.ChannelNotActive);
    }

    [Test]
    public async Task Method_not_allowed_by_channel_is_refused()
    {
        await using var fixture = new OutboundFixture();
        await fixture.CreateActiveChannelAsync();
        await fixture.RegisterProductAsync();

        using var db = fixture.CreateDb();
        await fixture.CreateProcessor(db).HandleAsync(Command(
            OutboundFixture.CreateRequest(method: "DELETE")));

        await Assert.That(fixture.Http.CallCount).IsEqualTo(0);

        var delivered = await fixture.Results.WaitForSingleAsync(DeliveryTimeout);

        await Assert.That(delivered.ErrorCode).IsEqualTo(OutboundErrorCodes.MethodNotAllowed);
    }

    [Test]
    public async Task Path_outside_allowed_templates_is_refused()
    {
        await using var fixture = new OutboundFixture();
        await fixture.CreateActiveChannelAsync();
        await fixture.RegisterProductAsync();

        using var db = fixture.CreateDb();
        await fixture.CreateProcessor(db).HandleAsync(Command(
            OutboundFixture.CreateRequest(relativePath: "/api/admin")));

        await Assert.That(fixture.Http.CallCount).IsEqualTo(0);

        var delivered = await fixture.Results.WaitForSingleAsync(DeliveryTimeout);

        await Assert.That(delivered.ErrorCode).IsEqualTo(OutboundErrorCodes.PathNotAllowed);
    }

    [Test]
    public async Task Path_with_absolute_url_is_refused()
    {
        await using var fixture = new OutboundFixture();
        await fixture.CreateActiveChannelAsync();
        await fixture.RegisterProductAsync();

        using var db = fixture.CreateDb();
        await fixture.CreateProcessor(db).HandleAsync(Command(
            OutboundFixture.CreateRequest(relativePath: "https://evil.example/api/contracts")));

        await Assert.That(fixture.Http.CallCount).IsEqualTo(0);

        var delivered = await fixture.Results.WaitForSingleAsync(DeliveryTimeout);

        await Assert.That(delivered.ErrorCode).IsEqualTo(OutboundErrorCodes.PathNotAllowed);
    }

    [Test]
    public async Task Path_parameter_template_matches_single_segment()
    {
        await using var fixture = new OutboundFixture();
        await fixture.CreateActiveChannelAsync();
        await fixture.RegisterProductAsync();

        fixture.Http.EnqueueResponse(HttpStatusCode.OK);

        using var db = fixture.CreateDb();
        await fixture.CreateProcessor(db).HandleAsync(Command(
            OutboundFixture.CreateRequest(relativePath: "/api/contracts/42")));

        await Assert.That(fixture.Http.CallCount).IsEqualTo(1);
        await Assert.That(fixture.Http.Requests[0].Uri).IsEqualTo("https://bank-a.example/api/contracts/42");
    }

    [Test]
    public async Task Journal_stores_bodies_and_marks_delivery_as_sent()
    {
        await using var fixture = new OutboundFixture();
        await fixture.CreateActiveChannelAsync();
        await fixture.RegisterProductAsync();

        fixture.Http.EnqueueResponse(HttpStatusCode.Created, "{\"id\":7}");

        using var db = fixture.CreateDb();
        await fixture.CreateProcessor(db).HandleAsync(Command(
            OutboundFixture.CreateRequest(
                method: "POST",
                relativePath: "/api/contracts",
                idempotencyKey: "idem-1",
                body: "{\"name\":\"x\"}"u8.ToArray(),
                contentType: "application/json")));

        await fixture.Results.WaitForSingleAsync(DeliveryTimeout);

        using var verifyDb = fixture.CreateDb();
        var log = await verifyDb.OutboundMessageLogs.SingleAsync();

        await Assert.That(System.Text.Encoding.UTF8.GetString(log.RequestBody)).IsEqualTo("{\"name\":\"x\"}");
        await Assert.That(System.Text.Encoding.UTF8.GetString(log.ResponseBody)).IsEqualTo("{\"id\":7}");
        await Assert.That(log.HttpStatusCode).IsEqualTo(201);
        await Assert.That(log.Outcome).IsEqualTo(OutcomeNames.HttpResponse);
        await Assert.That(log.DeliveryStatus).IsEqualTo(OutboundDeliveryStatus.Sent);
        await Assert.That(log.ChannelRevisionNumber).IsEqualTo(1);
        await Assert.That(log.CreatedAt).IsGreaterThan(DateTimeOffset.MinValue);

        var operation = await verifyDb.OutboundOperations.SingleAsync();

        await Assert.That(operation.Status).IsEqualTo(OutboundOperationStatus.Completed);
        await Assert.That(operation.Outcome).IsEqualTo(OutcomeNames.HttpResponse);
    }
}