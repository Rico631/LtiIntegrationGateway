using LtiIntegrationGateway.Features.Channels.UpdateChannelDraft;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LtiIntegrationGateway.Features.Channels.ActivateChannelRevision;

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

        var superseded = channel.Revisions
            .Where(entity => entity.Status == ChannelRevisionStatus.Active)
            .ToArray();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            foreach (var revision in superseded)
            {
                revision.Status = ChannelRevisionStatus.Superseded;
            }

            // Сначала фиксируем снятие предыдущей ревизии: частичный уникальный индекс не допускает
            // двух активных ревизий даже внутри одной транзакции.
            if (superseded.Length > 0)
            {
                await db.SaveChangesAsync(cancellationToken);
            }

            target.Status = ChannelRevisionStatus.Active;
            ChannelAuditWriter.Add(db, channel.Id, target.Number, "activate-channel-revision");
            await IntegrationRegistry.BumpAsync(db, channel.IntegrationId, cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // Гонка двух реплик: частичный уникальный индекс оставил активной только одну ревизию.
            await transaction.RollbackAsync(cancellationToken);

            return new ActivateChannelRevisionResult(
                target.Number,
                ["conflict: активная ревизия канала уже зафиксирована другой операцией"]);
        }

        return new ActivateChannelRevisionResult(target.Number, []);
    }

    private static bool IsUniqueViolation(Exception? exception) =>
        exception is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } ||
        (exception?.InnerException is not null && IsUniqueViolation(exception.InnerException));
}
