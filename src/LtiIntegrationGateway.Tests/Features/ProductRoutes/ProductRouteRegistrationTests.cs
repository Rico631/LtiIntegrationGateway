using LtiIntegrationGateway.Features.ProductRoutes;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities;
using LtiIntegrationGateway.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace LtiIntegrationGateway.Tests.Features.ProductRoutes;

/// <summary>
/// Регистрация и чтение маршрута продукта: адрес очереди вычисляется из <c>product_id</c>
/// как <c>&lt;product_id&gt;.inbox</c> (ADR §10.2).
/// </summary>
public class ProductRouteRegistrationTests
{
    private const string ProductId = "dpv";
    private const string ChannelName = "bank-a";

    [Test]
    public async Task Registration_without_address_saves_computed_inbox()
    {
        using var database = new SqliteInMemoryDatabase();
        database.Migrate();

        using (var db = database.CreateContext())
        {
            var result = await new RegisterProductRouteHandler(db).HandleAsync(
                new RegisterProductRouteCommand(ProductId, [ChannelName], []));

            await Assert.That(result.Problems.Count).IsEqualTo(0);
        }

        using var verifyDb = database.CreateContext();
        var addresses = await verifyDb.ProductRouteQueueAddresses
            .Select(address => address.Address)
            .ToArrayAsync();

        await Assert.That(addresses).IsEquivalentTo(new[] { "dpv.inbox" });
    }

    [Test]
    public async Task Registration_with_computed_address_is_accepted()
    {
        using var database = new SqliteInMemoryDatabase();
        database.Migrate();

        using (var db = database.CreateContext())
        {
            var result = await new RegisterProductRouteHandler(db).HandleAsync(
                new RegisterProductRouteCommand(ProductId, [ChannelName], ["dpv.inbox"]));

            await Assert.That(result.Problems.Count).IsEqualTo(0);
        }

        using var verifyDb = database.CreateContext();
        var addresses = await verifyDb.ProductRouteQueueAddresses
            .Select(address => address.Address)
            .ToArrayAsync();

        await Assert.That(addresses).IsEquivalentTo(new[] { "dpv.inbox" });
    }

    [Test]
    public async Task Registration_with_foreign_address_is_rejected_and_route_is_unchanged()
    {
        using var database = new SqliteInMemoryDatabase();
        database.Migrate();

        using (var db = database.CreateContext())
        {
            await new RegisterProductRouteHandler(db).HandleAsync(
                new RegisterProductRouteCommand(ProductId, [ChannelName], []));
        }

        using (var db = database.CreateContext())
        {
            var result = await new RegisterProductRouteHandler(db).HandleAsync(
                new RegisterProductRouteCommand(ProductId, [ChannelName], ["other.queue"]));

            await Assert.That(result.Problems.Count).IsEqualTo(1);
            await Assert.That(result.Problems[0].StartsWith("address:", StringComparison.Ordinal)).IsTrue();
        }

        using var verifyDb = database.CreateContext();
        var route = await verifyDb.ProductRoutes
            .Include(entity => entity.AllowedChannels)
            .Include(entity => entity.AllowedQueueAddresses)
            .SingleAsync();

        await Assert.That(route.AllowedChannels.Select(channel => channel.ChannelName).ToArray())
            .IsEquivalentTo(new[] { ChannelName });
        await Assert.That(route.AllowedQueueAddresses.Select(address => address.Address).ToArray())
            .IsEquivalentTo(new[] { "dpv.inbox" });
    }

    [Test]
    public async Task Read_returns_computed_address_for_legacy_record()
    {
        using var database = new SqliteInMemoryDatabase();
        database.Migrate();

        using (var db = database.CreateContext())
        {
            // Существующая запись с произвольным адресом: миграции нет, источник истины — расчёт.
            db.ProductRoutes.Add(new ProductRoute
            {
                Id = Guid.NewGuid(),
                ProductId = ProductId,
                CreatedAt = DateTimeOffset.UtcNow,
                AllowedChannels = [new ProductRouteChannel { Id = Guid.NewGuid(), ChannelName = ChannelName }],
                AllowedQueueAddresses = [new ProductRouteQueueAddress { Id = Guid.NewGuid(), Address = "legacy.queue" }],
            });

            await db.SaveChangesAsync();
        }

        using (var db = database.CreateContext())
        {
            var result = await new GetProductRouteHandler(db).HandleAsync(new GetProductRouteQuery(ProductId));

            await Assert.That(result.Route).IsNotNull();
            await Assert.That(result.Route!.AllowedQueueAddresses).IsEquivalentTo(new[] { "dpv.inbox" });
            await Assert.That(result.Route.AllowedChannels).IsEquivalentTo(new[] { ChannelName });
        }

        using var verifyDb = database.CreateContext();
        var stored = await verifyDb.ProductRouteQueueAddresses.SingleAsync();

        await Assert.That(stored.Address).IsEqualTo("legacy.queue");
    }
}
