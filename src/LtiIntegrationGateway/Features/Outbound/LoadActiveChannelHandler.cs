using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;
using Microsoft.EntityFrameworkCore;

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

public sealed class LoadActiveChannelHandler(GatewayDbContext db)
{
    public async Task<ActiveChannel?> HandleAsync(string channelName, CancellationToken cancellationToken = default)
    {
        var revision = await db.ChannelRevisions
            .AsNoTracking()
            .Include(entity => entity.Channel)
            .SingleOrDefaultAsync(
                entity => entity.Channel!.Name == channelName &&
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
