using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Rebus;
using Microsoft.EntityFrameworkCore;

namespace LtiIntegrationGateway.Features.ProductRoutes;

/// <summary>Чтение маршрута продукта.</summary>
public sealed record GetProductRouteQuery(string ProductId);

public sealed record ProductRouteDetails(
    string ProductId,
    IReadOnlyList<string> AllowedChannels,
    IReadOnlyList<string> AllowedQueueAddresses);

public sealed record GetProductRouteResult(ProductRouteDetails? Route, IReadOnlyList<string> Problems);

/// <summary>
/// Чтение маршрута продукта: адрес очереди всегда возвращается вычисленным из <c>product_id</c>
/// (ADR §10.2); сохранённые записи с произвольными адресами не мигрируются и не отдаются.
/// </summary>
public sealed class GetProductRouteHandler(GatewayDbContext db)
{
    public async Task<GetProductRouteResult> HandleAsync(
        GetProductRouteQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var route = await db.ProductRoutes
            .AsNoTracking()
            .Include(entity => entity.AllowedChannels)
            .SingleOrDefaultAsync(entity => entity.ProductId == query.ProductId, cancellationToken);

        if (route is null)
        {
            return new GetProductRouteResult(null, [$"product: продукт '{query.ProductId}' не найден"]);
        }

        var details = new ProductRouteDetails(
            route.ProductId,
            route.AllowedChannels.Select(entity => entity.ChannelName).ToArray(),
            [QueueNames.ProductInbox(route.ProductId)]);

        return new GetProductRouteResult(details, []);
    }
}
