using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Rebus;
using LtiIntegrationGateway.Infrastructure.Results;
using Microsoft.EntityFrameworkCore;

namespace LtiIntegrationGateway.Features.Outbound;

/// <summary>Проверенный продукт и разрешённые ему адреса результата.</summary>
public sealed record SenderIdentity(IReadOnlyCollection<string> AllowedAddresses);

/// <summary>
/// Проверка отправителя и адреса результата: адрес сравнивается с вычисленным
/// <c>&lt;product_id&gt;.inbox</c> (ADR §10.2). Список адресов в маршруте остаётся для обратной
/// совместимости чтения, но источником истины не является.
/// </summary>
public sealed class ResolveSenderIdentityHandler(GatewayDbContext db)
{
    public async Task<Result<SenderIdentity>> HandleAsync(
        string productId,
        string? senderAddress,
        string channelName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(senderAddress))
        {
            return Failure(
                OutboundErrorCodes.ProductNotAllowed,
                "заголовок адреса отправителя отсутствует");
        }

        var route = await db.ProductRoutes
            .AsNoTracking()
            .Include(entity => entity.AllowedChannels)
            .SingleOrDefaultAsync(entity => entity.ProductId == productId, cancellationToken);

        if (route is null)
        {
            return Failure(
                OutboundErrorCodes.ProductNotAllowed,
                $"продукт '{productId}' не зарегистрирован");
        }

        var expectedAddress = QueueNames.ProductInbox(productId);

        if (!string.Equals(senderAddress, expectedAddress, StringComparison.Ordinal))
        {
            return Failure(
                OutboundErrorCodes.ProductNotAllowed,
                "адрес отправителя не разрешён продукту");
        }

        var allowedChannels = route.AllowedChannels
            .Select(entity => entity.ChannelName)
            .ToArray();

        if (!allowedChannels.Contains(channelName, StringComparer.Ordinal))
        {
            return Failure(
                OutboundErrorCodes.ChannelNotAllowed,
                $"канал '{channelName}' не разрешён продукту");
        }

        return Result.Success(new SenderIdentity([expectedAddress]));
    }

    private static Result<SenderIdentity> Failure(string code, string message) =>
        Result.Failure<SenderIdentity>(code, message);
}

