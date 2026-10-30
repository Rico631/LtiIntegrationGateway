using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace LtiIntegrationGateway.Features.Outbound;

/// <summary>Активная ревизия канала с настройками, нужными для вызова.</summary>
public sealed record ActiveChannel(
    string ChannelName,
    string BaseUrl,
    ChannelAuthType AuthType,
    string? ConfigKey,
    string? HeaderName,
    string? UserName,
    int PerAttemptTimeoutMs,
    int OverallCapMs,
    RetryPolicy RetryPolicy,
    IReadOnlyCollection<string> AllowedMethods,
    IReadOnlyCollection<string> AllowedPaths);

public sealed class LoadActiveChannelHandler(GatewayDbContext db)
{
    public async Task<ActiveChannel?> HandleAsync(string channelName, CancellationToken cancellationToken = default)
    {
        var revision = await db.ChannelRevisions
            .AsNoTracking()
            .Include(entity => entity.AllowedMethods)
            .Include(entity => entity.AllowedPaths)
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
            revision.BaseUrl,
            revision.AuthType,
            revision.ConfigKey,
            revision.HeaderName,
            revision.UserName,
            revision.PerAttemptTimeoutMs,
            revision.OverallCapMs,
            revision.RetryPolicy,
            revision.AllowedMethods.Select(entity => entity.Method).ToArray(),
            revision.AllowedPaths.Select(entity => entity.Template).ToArray());
    }
}
