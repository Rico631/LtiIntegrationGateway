using LtiIntegrationGateway.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LtiIntegrationGateway.Features.ProductRoutes;

/// <summary>Чтение маршрута продукта.</summary>
public sealed record GetProductRouteQuery(string ProductId);

public sealed record ProductRouteDetails(
    string ProductId,
    IReadOnlyList<string> AllowedChannels,
    IReadOnlyList<string> AllowedQueueAddresses);

public sealed record GetProductRouteResult(ProductRouteDetails? Route, IReadOnlyList<string> Problems);

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
            .Include(entity => entity.AllowedQueueAddresses)
            .SingleOrDefaultAsync(entity => entity.ProductId == query.ProductId, cancellationToken);

        if (route is null)
        {
            return new GetProductRouteResult(null, [$"product: продукт '{query.ProductId}' не найден"]);
        }

        var details = new ProductRouteDetails(
            route.ProductId,
            route.AllowedChannels.Select(entity => entity.ChannelName).ToArray(),
            route.AllowedQueueAddresses.Select(entity => entity.Address).ToArray());

        return new GetProductRouteResult(details, []);
    }
}
