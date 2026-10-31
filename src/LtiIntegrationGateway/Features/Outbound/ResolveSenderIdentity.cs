using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Rebus;
using Microsoft.EntityFrameworkCore;

namespace LtiIntegrationGateway.Features.Outbound;

/// <summary>Проверка продукта и адресов по конфигурации маршрутов продуктов.</summary>
public sealed record SenderIdentityResult(
    string? ErrorCode,
    string? ErrorMessage,
    IReadOnlyCollection<string> AllowedAddresses);

/// <summary>
/// Проверка отправителя и адреса результата: адрес сравнивается с вычисленным
/// <c>&lt;product_id&gt;.inbox</c> (ADR §10.2). Список адресов в маршруте остаётся для обратной
/// совместимости чтения, но источником истины не является.
/// </summary>
public sealed class ResolveSenderIdentityHandler(GatewayDbContext db)
{
    public async Task<SenderIdentityResult> HandleAsync(
        string productId,
        string? senderAddress,
        string channelName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(senderAddress))
        {
            return new SenderIdentityResult(
                OutboundErrorCodes.ProductNotAllowed,
                "заголовок адреса отправителя отсутствует",
                []);
        }

        var route = await db.ProductRoutes
            .AsNoTracking()
            .Include(entity => entity.AllowedChannels)
            .SingleOrDefaultAsync(entity => entity.ProductId == productId, cancellationToken);

        if (route is null)
        {
            return new SenderIdentityResult(
                OutboundErrorCodes.ProductNotAllowed,
                $"продукт '{productId}' не зарегистрирован",
                []);
        }

        var expectedAddress = QueueNames.ProductInbox(productId);
        IReadOnlyCollection<string> addresses = [expectedAddress];

        if (!string.Equals(senderAddress, expectedAddress, StringComparison.Ordinal))
        {
            return new SenderIdentityResult(
                OutboundErrorCodes.ProductNotAllowed,
                "адрес отправителя не разрешён продукту",
                addresses);
        }

        var allowedChannels = route.AllowedChannels
            .Select(entity => entity.ChannelName)
            .ToArray();

        if (!allowedChannels.Contains(channelName, StringComparer.Ordinal))
        {
            return new SenderIdentityResult(
                OutboundErrorCodes.ChannelNotAllowed,
                $"канал '{channelName}' не разрешён продукту",
                addresses);
        }

        return new SenderIdentityResult(null, null, addresses);
    }
}

