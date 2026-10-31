using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities;
using LtiIntegrationGateway.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace LtiIntegrationGateway.Features.Channels;

/// <summary>Публикация черновика: ревизия становится неизменяемой.</summary>
public sealed record PublishChannelRevisionCommand(Guid ChannelId, int? RevisionNumber);

/// <summary>
/// Результат публикации: проблемы блокируют публикацию, предупреждения (например, отсутствующий
/// секрет) не мешают ревизии стать опубликованной.
/// </summary>
public sealed record PublishChannelRevisionResult(
    int? RevisionNumber,
    IReadOnlyList<string> Problems,
    IReadOnlyList<string> Warnings);

public sealed class PublishChannelRevisionHandler(
    GatewayDbContext db,
    ChannelConfigurationValidator validator,
    ISecretStore secrets,
    IChannelCertificateStore certificates)
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
            return new PublishChannelRevisionResult(null, ["channel: канал не найден"], []);
        }

        var revision = command.RevisionNumber is { } number
            ? channel.Revisions.SingleOrDefault(entity => entity.Number == number)
            : channel.Revisions.SingleOrDefault(entity => entity.Status == ChannelRevisionStatus.Draft);

        if (revision is null)
        {
            return new PublishChannelRevisionResult(null, ["revision: черновик не найден"], []);
        }

        if (revision.Status != ChannelRevisionStatus.Draft)
        {
            return new PublishChannelRevisionResult(
                revision.Number,
                ["revision_not_editable: публиковать можно только черновик"],
                []);
        }

        var configuration = ChannelRevisionWriter.Read(revision);
        var problems = validator.Validate(configuration);

        if (problems.Count > 0)
        {
            return new PublishChannelRevisionResult(revision.Number, problems, []);
        }

        revision.Status = ChannelRevisionStatus.Published;
        ChannelAuditWriter.Add(db, channel.Id, revision.Number, "publish-channel-revision");

        await db.SaveChangesAsync(cancellationToken);

        return new PublishChannelRevisionResult(revision.Number, [], CheckSecret(configuration));
    }

    /// <summary>
    /// Предупреждение не блокирует публикацию: ревизия может быть опубликована до настройки секрета,
    /// но рантайм не сможет выполнить исходящий вызов, пока значение не появится.
    /// </summary>
    private IReadOnlyList<string> CheckSecret(ChannelConfiguration configuration)
    {
        if (configuration.AuthType == ChannelAuthType.None)
        {
            return [];
        }

        var configured = configuration.AuthType == ChannelAuthType.ClientCertificate
            ? certificates.GetInfo(configuration.ConfigKey).Found
            : secrets.Find(configuration.ConfigKey ?? string.Empty) is not null;

        return configured
            ? []
            : [$"secret_key_missing: значение секрета по ключу '{configuration.ConfigKey}' не настроено"];
    }
}
