using System.Text.RegularExpressions;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace LtiIntegrationGateway.Features.ProductRoutes;

/// <summary>Создание или обновление маршрута продукта: разрешённые каналы и адреса очередей.</summary>
public sealed record RegisterProductRouteCommand(
    string ProductId,
    IReadOnlyList<string> AllowedChannels,
    IReadOnlyList<string> AllowedQueueAddresses);

public sealed record RegisterProductRouteResult(IReadOnlyList<string> Problems);

public sealed partial class RegisterProductRouteHandler(GatewayDbContext db)
{
    [GeneratedRegex("^[a-z][a-z0-9-]{1,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex ProductIdPattern();

    public async Task<RegisterProductRouteResult> HandleAsync(
        RegisterProductRouteCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(command.ProductId) || !ProductIdPattern().IsMatch(command.ProductId))
        {
            problems.Add("product_id: ожидается формат ^[a-z][a-z0-9-]{1,31}$");
        }

        if (command.AllowedChannels.Count == 0)
        {
            problems.Add("allowed_channels: должен быть указан хотя бы один канал");
        }

        if (command.AllowedQueueAddresses.Count == 0)
        {
            problems.Add("allowed_queue_addresses: должен быть указан хотя бы один адрес");
        }

        if (problems.Count > 0)
        {
            return new RegisterProductRouteResult(problems);
        }

        var route = await db.ProductRoutes
            .Include(entity => entity.AllowedChannels)
            .Include(entity => entity.AllowedQueueAddresses)
            .SingleOrDefaultAsync(entity => entity.ProductId == command.ProductId, cancellationToken);

        if (route is null)
        {
            route = new ProductRoute
            {
                Id = Guid.NewGuid(),
                ProductId = command.ProductId,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            db.ProductRoutes.Add(route);
        }

        route.AllowedChannels.Clear();
        route.AllowedQueueAddresses.Clear();

        foreach (var channel in command.AllowedChannels.Distinct(StringComparer.Ordinal))
        {
            route.AllowedChannels.Add(new ProductRouteChannel { Id = Guid.NewGuid(), ChannelName = channel });
        }

        foreach (var address in command.AllowedQueueAddresses.Distinct(StringComparer.Ordinal))
        {
            route.AllowedQueueAddresses.Add(new ProductRouteQueueAddress { Id = Guid.NewGuid(), Address = address });
        }

        await db.SaveChangesAsync(cancellationToken);

        return new RegisterProductRouteResult([]);
    }
}
