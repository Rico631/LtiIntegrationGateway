using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace LtiIntegrationGateway.Features.Channels;

/// <summary>Публикация черновика: ревизия становится неизменяемой.</summary>
public sealed record PublishChannelRevisionCommand(Guid ChannelId, int? RevisionNumber);

public sealed record PublishChannelRevisionResult(int? RevisionNumber, IReadOnlyList<string> Problems);

public sealed class PublishChannelRevisionHandler(GatewayDbContext db, ChannelConfigurationValidator validator)
{
    public async Task<PublishChannelRevisionResult> HandleAsync(
        PublishChannelRevisionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var channel = await ChannelQueries
            .WithRevisions(db)
            .SingleOrDefaultAsync(entity => entity.Id == command.ChannelId, cancellationToken);

        if (channel is null)
        {
            return new PublishChannelRevisionResult(null, ["channel: канал не найден"]);
        }

        var revision = command.RevisionNumber is { } number
            ? channel.Revisions.SingleOrDefault(entity => entity.Number == number)
            : channel.Revisions.SingleOrDefault(entity => entity.Status == ChannelRevisionStatus.Draft);

        if (revision is null)
        {
            return new PublishChannelRevisionResult(null, ["revision: черновик не найден"]);
        }

        if (revision.Status != ChannelRevisionStatus.Draft)
        {
            return new PublishChannelRevisionResult(
                revision.Number,
                ["revision_not_editable: публиковать можно только черновик"]);
        }

        var problems = validator.Validate(ChannelRevisionWriter.Read(revision));

        if (problems.Count > 0)
        {
            return new PublishChannelRevisionResult(revision.Number, problems);
        }

        revision.Status = ChannelRevisionStatus.Published;
        ChannelAuditWriter.Add(db, channel.Id, revision.Number, "publish-channel-revision");

        await db.SaveChangesAsync(cancellationToken);

        return new PublishChannelRevisionResult(revision.Number, []);
    }
}
