using System.Text.RegularExpressions;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.ProductRoutes;
using LtiIntegrationGateway.Infrastructure.Rebus;
using Microsoft.EntityFrameworkCore;

namespace LtiIntegrationGateway.Features.ProductRoutes;

/// <summary>Создание или обновление маршрута продукта: разрешённые каналы и адрес очереди продукта.</summary>
public sealed record RegisterProductRouteCommand(
    string ProductId,
    IReadOnlyList<string> AllowedChannels,
    IReadOnlyList<string> AllowedQueueAddresses);

public sealed record RegisterProductRouteResult(IReadOnlyList<string> Problems);

/// <summary>
/// Регистрация маршрута продукта: адрес очереди вычисляется из <c>product_id</c> как
/// <c>&lt;product_id&gt;.inbox</c> (ADR §10.2). Пустой список адресов означает вычисленный адрес,
/// совпадающий адрес принимается идемпотентно, несовпадающий — проблема <c>address</c> без изменения
/// маршрута.
/// </summary>
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

        var productIdValid = !string.IsNullOrWhiteSpace(command.ProductId) &&
            ProductIdPattern().IsMatch(command.ProductId);

        if (!productIdValid)
        {
            problems.Add("product_id: ожидается формат ^[a-z][a-z0-9-]{1,31}$");
        }

        if (command.AllowedChannels.Count == 0)
        {
            problems.Add("allowed_channels: должен быть указан хотя бы один канал");
        }
        else
        {
            var requested = command.AllowedChannels
                .Where(channel => !string.IsNullOrWhiteSpace(channel))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            var existing = await db.OutboundChannels
                .Where(channel => requested.Contains(channel.Name))
                .Select(channel => channel.Name)
                .ToListAsync(cancellationToken);

            foreach (var missing in requested.Except(existing, StringComparer.Ordinal))
            {
                problems.Add($"allowed_channels: outbound-канал '{missing}' не найден");
            }
        }

        if (productIdValid)
        {
            var expectedAddress = QueueNames.ProductInbox(command.ProductId);

            // Список остаётся в контракте для совместимости, но единственный допустимый адрес —
            // вычисленный: произвольные значения отклоняются, чтобы конфигурация не расходилась
            // с именем очереди (ADR §10.2).
            if (command.AllowedQueueAddresses.Any(
                address => !string.Equals(address, expectedAddress, StringComparison.Ordinal)))
            {
                problems.Add($"address: адрес очереди продукта должен совпадать с вычисленным '{expectedAddress}'");
            }
        }

        if (problems.Count > 0)
        {
            return new RegisterProductRouteResult(problems);
        }

        var address = QueueNames.ProductInbox(command.ProductId);

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

        // Существующие записи с произвольными адресами не мигрируются, но повторная регистрация
        // приводит маршрут к вычисленному адресу.
        route.AllowedQueueAddresses.Add(new ProductRouteQueueAddress { Id = Guid.NewGuid(), Address = address });

        await db.SaveChangesAsync(cancellationToken);

        return new RegisterProductRouteResult([]);
    }
}

