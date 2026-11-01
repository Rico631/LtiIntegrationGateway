using LtiIntegrationGateway.Contracts.Messages.V1;
using LtiIntegrationGateway.Features.Outbound;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Outbound;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.ProductRoutes;
using LtiIntegrationGateway.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace LtiIntegrationGateway.Tests.Features.Isolation;

/// <summary>
/// Изоляция интеграций в исходящем контуре: экземпляр не использует чужие каналы, маршруты и
/// операции, а одинаковый <c>message_id</c> в другой интеграции — независимая операция.
/// </summary>
public class OutboundIntegrationIsolationTests
{
    private const string Foreign = "bank-b";
    private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(5);

    private static ProcessOutboundRequestCommand Command(OutboundRequest? request = null) =>
        new(
            request ?? OutboundFixture.CreateRequest(),
            OutboundFixture.ProductInbox,
            OutboundFixture.ProductInbox);

    [Test]
    public async Task Foreign_channel_with_active_revision_is_refused_as_not_allowed()
    {
        await using var fixture = new OutboundFixture();
        await fixture.CreateForeignChannelAsync(Foreign, "foreign");

        // Маршрут продукта своей интеграции разрешает чужой канал: проверку владения выполняет
        // экземпляр, а не управляющий API.
        using (var db = fixture.CreateDb())
        {
            db.ProductRoutes.Add(new ProductRoute
            {
                Id = Guid.NewGuid(),
                IntegrationId = OutboundFixture.IntegrationId,
                ProductId = OutboundFixture.ProductId,
                CreatedAt = DateTimeOffset.UtcNow,
                AllowedChannels = { new ProductRouteChannel { Id = Guid.NewGuid(), ChannelName = "foreign" } },
            });

            await db.SaveChangesAsync();
        }

        using var processingDb = fixture.CreateDb();
        await fixture.CreateProcessor(processingDb)
            .HandleAsync(Command(OutboundFixture.CreateRequest(channelName: "foreign")));

        await Assert.That(fixture.Http.CallCount).IsEqualTo(0);

        var delivered = await fixture.Results.WaitForSingleAsync(DeliveryTimeout);

        await Assert.That(delivered.Outcome).IsEqualTo(Outcome.GatewayRefused);
        await Assert.That(delivered.ErrorCode).IsEqualTo(OutboundErrorCodes.ChannelNotAllowed);
    }

    [Test]
    public async Task Product_route_registered_only_in_another_integration_does_not_authorise_request()
    {
        await using var fixture = new OutboundFixture();
        await fixture.CreateActiveChannelAsync();
        await fixture.CreateForeignChannelAsync(Foreign, "foreign");
        await fixture.RegisterProductForIntegrationAsync(Foreign, ["foreign"]);

        using var db = fixture.CreateDb();
        await fixture.CreateProcessor(db).HandleAsync(Command());

        await Assert.That(fixture.Http.CallCount).IsEqualTo(0);

        using var verifyDb = fixture.CreateDb();
        var log = await verifyDb.OutboundMessageLogs.SingleAsync();

        await Assert.That(log.ErrorCode).IsEqualTo(OutboundErrorCodes.ProductNotAllowed);
    }

    [Test]
    public async Task Same_message_id_in_another_integration_is_an_independent_operation()
    {
        await using var fixture = new OutboundFixture();
        await fixture.CreateActiveChannelAsync();
        await fixture.RegisterProductAsync();

        var createdAt = DateTimeOffset.UtcNow;

        using (var db = fixture.CreateDb())
        {
            db.OutboundOperations.Add(new OutboundOperation
            {
                Id = Guid.NewGuid(),
                IntegrationId = Foreign,
                MessageId = "msg-1",
                ProductId = OutboundFixture.ProductId,
                ChannelName = "foreign",
                Status = OutboundOperationStatus.Completed,
                Outcome = OutcomeNames.HttpResponse,
                HttpStatusCode = 200,
                AttemptCount = 1,
                CreatedAt = createdAt,
                CompletedAt = createdAt,
            });

            await db.SaveChangesAsync();
        }

        fixture.Http.EnqueueResponse(System.Net.HttpStatusCode.OK, """{"ok":true}""");

        using var processingDb = fixture.CreateDb();
        var result = await fixture.CreateProcessor(processingDb).HandleAsync(Command());

        await Assert.That(result.Completed).IsTrue();
        await Assert.That(fixture.Http.CallCount).IsEqualTo(1);

        var delivered = await fixture.Results.WaitForSingleAsync(DeliveryTimeout);

        await Assert.That(delivered.Outcome).IsEqualTo(Outcome.HttpResponse);

        using var verifyDb = fixture.CreateDb();
        var foreignOperation = await verifyDb.OutboundOperations
            .SingleAsync(operation => operation.IntegrationId == Foreign);

        await Assert.That(foreignOperation.AttemptCount).IsEqualTo(1);
        await Assert.That(await verifyDb.OutboundOperations.CountAsync()).IsEqualTo(2);
    }
}
