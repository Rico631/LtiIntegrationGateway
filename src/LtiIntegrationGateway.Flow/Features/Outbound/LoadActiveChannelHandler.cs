using LtiIntegrationGateway.Features.Configuration;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LtiIntegrationGateway.Features.Outbound;

/// <summary>Активная ревизия канала с настройками, нужными для вызова.</summary>
public sealed record ActiveChannel(
    string ChannelName,
    int Number,
    string BaseUrl,
    ChannelAuthType AuthType,
    string? ConfigKey,
    string? HeaderName,
    string? UserName,
    int PerAttemptTimeoutMs,
    int OverallCapMs,
    RetryPolicy RetryPolicy);

public sealed class LoadActiveChannelHandler(GatewayDbContext db, IOptions<FlowOptions> options)
{
    /// <summary>
    /// Признак того, что канал с таким именем принадлежит другой интеграции: экземпляр обязан
    /// ответить <c>CHANNEL_NOT_ALLOWED</c>, а не сообщать об отсутствии активной ревизии.
    /// </summary>
    public Task<bool> ExistsInOtherIntegrationAsync(
        string channelName,
        CancellationToken cancellationToken = default)
    {
        var integrationId = options.Value.Integration;

        return db.OutboundChannels
            .AsNoTracking()
            .AnyAsync(
                channel => channel.Name == channelName && channel.IntegrationId != integrationId,
                cancellationToken);
    }

    /// <summary>
    /// Возвращает активную ревизию канала своей интеграции; канал другой интеграции считается
    /// ненайденным, поэтому внешний вызов по нему не выполняется.
    /// </summary>
    public async Task<ActiveChannel?> HandleAsync(string channelName, CancellationToken cancellationToken = default)
    {
        var integrationId = options.Value.Integration;

        var revision = await db.ChannelRevisions
            .AsNoTracking()
            .Include(entity => entity.OutboundChannel)
            .SingleOrDefaultAsync(
                entity => entity.OutboundChannel!.IntegrationId == integrationId &&
                          entity.OutboundChannel!.Name == channelName &&
                          entity.Status == ChannelRevisionStatus.Active,
                cancellationToken);

        if (revision is null)
        {
            return null;
        }

        return new ActiveChannel(
            channelName,
            revision.Number,
            revision.BaseUrl,
            revision.AuthType,
            revision.ConfigKey,
            revision.HeaderName,
            revision.UserName,
            revision.PerAttemptTimeoutMs,
            revision.OverallCapMs,
            revision.RetryPolicy);
    }
}
