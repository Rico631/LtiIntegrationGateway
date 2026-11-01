using LtiIntegrationGateway.Features.Channels.UpdateChannelDraft;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;
using Microsoft.EntityFrameworkCore;

namespace LtiIntegrationGateway.Features.Channels.GetChannel;

/// <summary>Чтение канала вместе с активной ревизией и перечнем ревизий.</summary>
public sealed record GetChannelQuery(Guid ChannelId);

public sealed record ChannelRevisionSummary(int Number, ChannelRevisionStatus Status);

public sealed record ChannelRevisionDetails(int Number, ChannelConfiguration Configuration);

public sealed record ChannelDetails(
    Guid Id,
    string Name,
    ChannelRevisionDetails? ActiveRevision,
    IReadOnlyList<ChannelRevisionSummary> Revisions);

public sealed record GetChannelResult(ChannelDetails? Channel, IReadOnlyList<string> Problems);

public sealed class GetChannelHandler(GatewayDbContext db)
{
    public async Task<GetChannelResult> HandleAsync(
        GetChannelQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var channel = await ChannelQueries
            .WithRevisions(db)
            .AsNoTracking()
            .SingleOrDefaultAsync(entity => entity.Id == query.ChannelId, cancellationToken);

        if (channel is null)
        {
            return new GetChannelResult(null, ["channel: канал не найден"]);
        }

        var active = channel.Revisions.SingleOrDefault(revision => revision.Status == ChannelRevisionStatus.Active);

        var details = new ChannelDetails(
            channel.Id,
            channel.Name,
            active is null ? null : new ChannelRevisionDetails(active.Number, ChannelRevisionWriter.Read(active)),
            channel.Revisions
                .OrderBy(revision => revision.Number)
                .Select(revision => new ChannelRevisionSummary(revision.Number, revision.Status))
                .ToArray());

        return new GetChannelResult(details, []);
    }
}
