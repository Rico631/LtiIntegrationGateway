using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;
using Microsoft.EntityFrameworkCore;

namespace LtiIntegrationGateway.Features.Channels.CreateChannel;

/// <summary>Создание канала: канал появляется вместе с черновиком ревизии.</summary>
public sealed record CreateChannelCommand(
    string Name,
    ChannelConfiguration Configuration,
    string IntegrationId = IntegrationDefaults.Default);

public sealed record CreateChannelResult(Guid? ChannelId, int? RevisionNumber, IReadOnlyList<string> Problems);

public sealed class CreateChannelHandler(GatewayDbContext db)
{
    public async Task<CreateChannelResult> HandleAsync(
        CreateChannelCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(command.Name))
        {
            problems.Add("name: обязательное поле не заполнено");
        }
        else if (await db.OutboundChannels.AnyAsync(
                     channel => channel.IntegrationId == command.IntegrationId && channel.Name == command.Name,
                     cancellationToken))
        {
            problems.Add("name: канал с таким именем уже существует");
        }

        if (problems.Count > 0)
        {
            return new CreateChannelResult(null, null, problems);
        }

        var channel = new OutboundChannel
        {
            Id = Guid.NewGuid(),
            IntegrationId = command.IntegrationId,
            Name = command.Name,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var revision = ChannelRevisionWriter.CreateDraft(channel.Id, number: 1, command.Configuration);
        channel.Revisions.Add(revision);

        db.OutboundChannels.Add(channel);
        ChannelAuditWriter.Add(db, channel.Id, revision.Number, "create-channel");
        await IntegrationRegistry.BumpAsync(db, command.IntegrationId, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return new CreateChannelResult(channel.Id, revision.Number, []);
    }
}
