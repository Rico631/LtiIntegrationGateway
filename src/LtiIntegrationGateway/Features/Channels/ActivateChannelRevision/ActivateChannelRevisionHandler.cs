using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace LtiIntegrationGateway.Features.Channels;

/// <summary>Активация опубликованной ревизии: предыдущая активная становится заменённой.</summary>
public sealed record ActivateChannelRevisionCommand(Guid ChannelId, int RevisionNumber);

public sealed record ActivateChannelRevisionResult(int? RevisionNumber, IReadOnlyList<string> Problems);

public sealed class ActivateChannelRevisionHandler(GatewayDbContext db)
{
    public async Task<ActivateChannelRevisionResult> HandleAsync(
        ActivateChannelRevisionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var channel = await ChannelQueries
            .WithRevisions(db)
            .SingleOrDefaultAsync(entity => entity.Id == command.ChannelId, cancellationToken);

        if (channel is null)
        {
            return new ActivateChannelRevisionResult(null, ["channel: канал не найден"]);
        }

        var target = channel.Revisions.SingleOrDefault(revision => revision.Number == command.RevisionNumber);

        if (target is null)
        {
            return new ActivateChannelRevisionResult(
                null,
                [$"revision: ревизия {command.RevisionNumber} не найдена"]);
        }

        if (target.Status == ChannelRevisionStatus.Draft)
        {
            return new ActivateChannelRevisionResult(
                target.Number,
                ["revision_not_published: черновик нельзя активировать"]);
        }

        foreach (var revision in channel.Revisions.Where(entity => entity.Status == ChannelRevisionStatus.Active))
        {
            revision.Status = ChannelRevisionStatus.Superseded;
        }

        target.Status = ChannelRevisionStatus.Active;
        ChannelAuditWriter.Add(db, channel.Id, target.Number, "activate-channel-revision");

        await db.SaveChangesAsync(cancellationToken);

        return new ActivateChannelRevisionResult(target.Number, []);
    }
}
