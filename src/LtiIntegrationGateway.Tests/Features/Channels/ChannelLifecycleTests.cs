using LtiIntegrationGateway.Features.Channels;
using LtiIntegrationGateway.Features.Channels.ActivateChannelRevision;
using LtiIntegrationGateway.Features.Channels.CreateChannel;
using LtiIntegrationGateway.Features.Channels.GetChannel;
using LtiIntegrationGateway.Features.Channels.PublishChannelRevision;
using LtiIntegrationGateway.Features.Channels.RollbackChannelRevision;
using LtiIntegrationGateway.Features.Channels.UpdateChannelDraft;

using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;
using LtiIntegrationGateway.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace LtiIntegrationGateway.Tests.Features.Channels;

public class ChannelLifecycleTests
{
    [Test]
    public async Task Created_channel_is_draft_without_active_revision()
    {
        using var database = new SqliteInMemoryDatabase();
        database.Migrate();

        Guid channelId;

        using (var db = database.CreateContext())
        {
            var result = await new CreateChannelHandler(db).HandleAsync(
                new CreateChannelCommand(ChannelTestData.ChannelName, ChannelTestData.ValidConfiguration()));

            await Assert.That(string.Join("; ", result.Problems)).IsEqualTo(string.Empty);
            await Assert.That(result.ChannelId).IsNotNull();
            await Assert.That(result.RevisionNumber).IsEqualTo(1);

            channelId = result.ChannelId!.Value;
        }

        using (var db = database.CreateContext())
        {
            var channel = await new GetChannelHandler(db).HandleAsync(new GetChannelQuery(channelId));

            await Assert.That(channel.Channel!.ActiveRevision).IsNull();
            await Assert.That(channel.Channel.Revisions.Count).IsEqualTo(1);
            await Assert.That(channel.Channel.Revisions[0].Status).IsEqualTo(ChannelRevisionStatus.Draft);
        }
    }

    [Test]
    public async Task Duplicate_channel_name_is_rejected_and_existing_channel_is_unchanged()
    {
        using var database = new SqliteInMemoryDatabase();
        database.Migrate();

        using var db = database.CreateContext();
        var handler = new CreateChannelHandler(db);

        var first = await handler.HandleAsync(
            new CreateChannelCommand(ChannelTestData.ChannelName, ChannelTestData.ValidConfiguration()));

        var second = await handler.HandleAsync(new CreateChannelCommand(
            ChannelTestData.ChannelName,
            ChannelTestData.ValidConfiguration("https://other.example")));

        await Assert.That(first.ChannelId).IsNotNull();
        await Assert.That(second.ChannelId).IsNull();
        await Assert.That(second.Problems.Any(problem => problem.StartsWith("name", StringComparison.Ordinal))).IsTrue();
        await Assert.That(await db.Channels.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task Published_revision_cannot_be_modified()
    {
        using var database = new SqliteInMemoryDatabase();
        database.Migrate();

        Guid channelId;

        using (var db = database.CreateContext())
        {
            var created = await new CreateChannelHandler(db).HandleAsync(
                new CreateChannelCommand(ChannelTestData.ChannelName, ChannelTestData.ValidConfiguration()));

            channelId = created.ChannelId!.Value;

            var published = await new PublishChannelRevisionHandler(
                    db,
                    new ChannelConfigurationValidator(),
                    new TestSecretStore(),
                    new TestChannelCertificateStore())
                .HandleAsync(new PublishChannelRevisionCommand(channelId, 1));

            await Assert.That(string.Join("; ", published.Problems)).IsEqualTo(string.Empty);
        }

        using (var db = database.CreateContext())
        {
            var update = await new UpdateChannelDraftHandler(db).HandleAsync(new UpdateChannelDraftCommand(
                channelId,
                RevisionNumber: 1,
                ChannelTestData.ValidConfiguration("https://changed.example")));

            await Assert
                .That(update.Problems.Any(problem => problem.StartsWith("revision_not_editable", StringComparison.Ordinal)))
                .IsTrue();
        }

        using (var db = database.CreateContext())
        {
            var revision = await db.ChannelRevisions.SingleAsync();

            await Assert.That(revision.Status).IsEqualTo(ChannelRevisionStatus.Published);
            await Assert.That(revision.BaseUrl).IsEqualTo("https://bank-a.example");
        }
    }

    [Test]
    public async Task Activation_switches_active_revision_and_rollback_restores_previous()
    {
        using var database = new SqliteInMemoryDatabase();
        database.Migrate();

        Guid channelId;

        using (var db = database.CreateContext())
        {
            var created = await new CreateChannelHandler(db).HandleAsync(
                new CreateChannelCommand(ChannelTestData.ChannelName, ChannelTestData.ValidConfiguration()));
            channelId = created.ChannelId!.Value;

            var publish = new PublishChannelRevisionHandler(
                db,
                new ChannelConfigurationValidator(),
                new TestSecretStore(),
                new TestChannelCertificateStore());
            var activate = new ActivateChannelRevisionHandler(db);

            await publish.HandleAsync(new PublishChannelRevisionCommand(channelId, 1));
            await activate.HandleAsync(new ActivateChannelRevisionCommand(channelId, 1));

            var draft = await new UpdateChannelDraftHandler(db).HandleAsync(new UpdateChannelDraftCommand(
                channelId, null, ChannelTestData.ValidConfiguration("https://bank-a-2.example")));
            await Assert.That(draft.RevisionNumber).IsEqualTo(2);

            await publish.HandleAsync(new PublishChannelRevisionCommand(channelId, 2));
            await activate.HandleAsync(new ActivateChannelRevisionCommand(channelId, 2));
        }

        using (var db = database.CreateContext())
        {
            var channel = await new GetChannelHandler(db).HandleAsync(new GetChannelQuery(channelId));

            await Assert.That(channel.Channel!.ActiveRevision!.Number).IsEqualTo(2);
            await Assert
                .That(channel.Channel.Revisions.Single(revision => revision.Number == 1).Status)
                .IsEqualTo(ChannelRevisionStatus.Superseded);
        }

        using (var db = database.CreateContext())
        {
            var rollback = await new RollbackChannelRevisionHandler(db)
                .HandleAsync(new RollbackChannelRevisionCommand(channelId));

            await Assert.That(string.Join("; ", rollback.Problems)).IsEqualTo(string.Empty);
            await Assert.That(rollback.RevisionNumber).IsEqualTo(1);
        }

        using (var db = database.CreateContext())
        {
            var channel = await new GetChannelHandler(db).HandleAsync(new GetChannelQuery(channelId));

            await Assert.That(channel.Channel!.ActiveRevision!.Number).IsEqualTo(1);
            await Assert.That(channel.Channel.Revisions.Count).IsEqualTo(2);
            await Assert
                .That(channel.Channel.Revisions.Single(revision => revision.Number == 2).Status)
                .IsEqualTo(ChannelRevisionStatus.Superseded);
        }
    }

    [Test]
    public async Task Rejected_publication_returns_all_reasons_and_keeps_active_revision()
    {
        using var database = new SqliteInMemoryDatabase();
        database.Migrate();

        Guid channelId;

        using (var db = database.CreateContext())
        {
            var created = await new CreateChannelHandler(db).HandleAsync(
                new CreateChannelCommand(ChannelTestData.ChannelName, ChannelTestData.ValidConfiguration()));
            channelId = created.ChannelId!.Value;

            var publish = new PublishChannelRevisionHandler(
                db,
                new ChannelConfigurationValidator(),
                new TestSecretStore(),
                new TestChannelCertificateStore());

            await publish.HandleAsync(new PublishChannelRevisionCommand(channelId, 1));
            await new ActivateChannelRevisionHandler(db)
                .HandleAsync(new ActivateChannelRevisionCommand(channelId, 1));

            await new UpdateChannelDraftHandler(db).HandleAsync(new UpdateChannelDraftCommand(
                channelId,
                null,
                ChannelTestData.ValidConfiguration("http://bank-a.example") with
                {
                    ConfigKey = null,
                    RetryPolicy = new RetryPolicy([]),
                }));

            var publishResult = await publish.HandleAsync(new PublishChannelRevisionCommand(channelId, 2));

            await Assert.That(publishResult.Problems.Count >= 3).IsTrue();
        }

        using (var db = database.CreateContext())
        {
            var channel = await new GetChannelHandler(db).HandleAsync(new GetChannelQuery(channelId));

            await Assert.That(channel.Channel!.ActiveRevision!.Number).IsEqualTo(1);
            await Assert
                .That(channel.Channel.Revisions.Single(revision => revision.Number == 2).Status)
                .IsEqualTo(ChannelRevisionStatus.Draft);
        }
    }
}