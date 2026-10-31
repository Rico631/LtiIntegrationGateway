using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;
using Microsoft.EntityFrameworkCore;

namespace LtiIntegrationGateway.Features.Channels.UpdateChannelDraft;

/// <summary>Изменение настроек черновика; опубликованные ревизии не изменяются.</summary>
public sealed record UpdateChannelDraftCommand(
    Guid ChannelId,
    int? RevisionNumber,
    ChannelConfiguration Configuration);

public sealed record UpdateChannelDraftResult(int? RevisionNumber, IReadOnlyList<string> Problems);

public sealed class UpdateChannelDraftHandler(GatewayDbContext db)
{
    public async Task<UpdateChannelDraftResult> HandleAsync(
        UpdateChannelDraftCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var channel = await ChannelQueries
            .WithRevisions(db)
            .SingleOrDefaultAsync(entity => entity.Id == command.ChannelId, cancellationToken);

        if (channel is null)
        {
            return new UpdateChannelDraftResult(null, ["channel: канал не найден"]);
        }

        ChannelRevision revision;

        if (command.RevisionNumber is { } number)
        {
            var requested = channel.Revisions.SingleOrDefault(entity => entity.Number == number);

            if (requested is null)
            {
                return new UpdateChannelDraftResult(null, [$"revision: ревизия {number} не найдена"]);
            }

            if (requested.Status != ChannelRevisionStatus.Draft)
            {
                return new UpdateChannelDraftResult(
                    requested.Number,
                    ["revision_not_editable: опубликованная ревизия не изменяется"]);
            }

            revision = requested;
        }
        else
        {
            var existingDraft = channel.Revisions.SingleOrDefault(
                entity => entity.Status == ChannelRevisionStatus.Draft);

            if (existingDraft is null)
            {
                var nextNumber = channel.Revisions.Count == 0
                    ? 1
                    : channel.Revisions.Max(entity => entity.Number) + 1;

                var draft = ChannelRevisionWriter.CreateDraft(channel.Id, nextNumber, command.Configuration);
                db.ChannelRevisions.Add(draft);
                ChannelAuditWriter.Add(db, channel.Id, draft.Number, "create-draft");

                await db.SaveChangesAsync(cancellationToken);

                return new UpdateChannelDraftResult(draft.Number, []);
            }

            revision = existingDraft;
        }

        ChannelRevisionWriter.Apply(revision, command.Configuration);
        ChannelAuditWriter.Add(db, channel.Id, revision.Number, "update-channel-draft");

        await db.SaveChangesAsync(cancellationToken);

        return new UpdateChannelDraftResult(revision.Number, []);
    }
}

/// <summary>Общие запросы чтения канала вместе с настройками ревизий.</summary>
internal static class ChannelQueries
{
    public static IQueryable<Channel> WithRevisions(GatewayDbContext db) =>
        db.Channels
            .Include(channel => channel.Revisions)
            .AsSplitQuery();
}
