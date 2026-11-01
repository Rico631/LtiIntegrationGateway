using LtiIntegrationGateway.Features.Channels.UpdateChannelDraft;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;
using Microsoft.EntityFrameworkCore;

namespace LtiIntegrationGateway.Features.Channels.RollbackChannelRevision;

/// <summary>Откат к предыдущей ревизии без удаления текущей.</summary>
public sealed record RollbackChannelRevisionCommand(Guid ChannelId);

public sealed record RollbackChannelRevisionResult(int? RevisionNumber, IReadOnlyList<string> Problems);

public sealed class RollbackChannelRevisionHandler(GatewayDbContext db)
{
    public async Task<RollbackChannelRevisionResult> HandleAsync(
        RollbackChannelRevisionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var channel = await ChannelQueries
            .WithRevisions(db)
            .SingleOrDefaultAsync(entity => entity.Id == command.ChannelId, cancellationToken);

        if (channel is null)
        {
            return new RollbackChannelRevisionResult(null, ["channel: канал не найден"]);
        }

        var active = channel.Revisions.SingleOrDefault(revision => revision.Status == ChannelRevisionStatus.Active);

        if (active is null)
        {
            return new RollbackChannelRevisionResult(null, ["active_revision: активная ревизия отсутствует"]);
        }

        var previous = channel.Revisions
            .Where(revision => revision.Number < active.Number && revision.Status != ChannelRevisionStatus.Draft)
            .OrderByDescending(revision => revision.Number)
            .FirstOrDefault();

        if (previous is null)
        {
            return new RollbackChannelRevisionResult(
                active.Number,
                ["previous_revision: предыдущая ревизия отсутствует"]);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        active.Status = ChannelRevisionStatus.Superseded;
        await db.SaveChangesAsync(cancellationToken);

        previous.Status = ChannelRevisionStatus.Active;
        ChannelAuditWriter.Add(db, channel.Id, previous.Number, "rollback-channel-revision");
        await IntegrationRegistry.BumpAsync(db, channel.IntegrationId, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new RollbackChannelRevisionResult(previous.Number, []);
    }
}
