using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace LtiIntegrationGateway.Features.Channels;

/// <summary>Создание канала: канал появляется вместе с черновиком ревизии.</summary>
public sealed record CreateChannelCommand(string Name, ChannelConfiguration Configuration);

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
        else if (await db.Channels.AnyAsync(channel => channel.Name == command.Name, cancellationToken))
        {
            problems.Add("name: канал с таким именем уже существует");
        }

        if (problems.Count > 0)
        {
            return new CreateChannelResult(null, null, problems);
        }

        var channel = new Channel
        {
            Id = Guid.NewGuid(),
            Name = command.Name,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var revision = ChannelRevisionWriter.CreateDraft(channel.Id, number: 1, command.Configuration);
        channel.Revisions.Add(revision);

        db.Channels.Add(channel);
        ChannelAuditWriter.Add(db, channel.Id, revision.Number, "create-channel");

        await db.SaveChangesAsync(cancellationToken);

        return new CreateChannelResult(channel.Id, revision.Number, []);
    }
}
