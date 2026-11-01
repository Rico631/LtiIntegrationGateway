using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;

namespace LtiIntegrationGateway.Features.Channels;

/// <summary>Перенос настроек в сущность ревизии и обратно.</summary>
internal static class ChannelRevisionWriter
{
    public static ChannelRevision CreateDraft(Guid channelId, int number, ChannelConfiguration configuration)
    {
        var revision = new ChannelRevision
        {
            Id = Guid.NewGuid(),
            OutboundChannelId = channelId,
            Number = number,
            Status = ChannelRevisionStatus.Draft,
            BaseUrl = configuration.BaseUrl,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        ApplyValues(revision, configuration);

        return revision;
    }

    /// <summary>Переносит настройки в существующую ревизию.</summary>
    public static void Apply(ChannelRevision revision, ChannelConfiguration configuration) =>
        ApplyValues(revision, configuration);

    public static ChannelConfiguration Read(ChannelRevision revision) => new(
        revision.BaseUrl,
        revision.AuthType,
        revision.ConfigKey,
        revision.HeaderName,
        revision.UserName,
        revision.PerAttemptTimeoutMs,
        revision.OverallCapMs,
        revision.RetryPolicy);

    private static void ApplyValues(ChannelRevision revision, ChannelConfiguration configuration)
    {
        revision.BaseUrl = configuration.BaseUrl;
        revision.AuthType = configuration.AuthType;
        revision.ConfigKey = configuration.ConfigKey;
        revision.HeaderName = configuration.HeaderName;
        revision.UserName = configuration.UserName;
        revision.PerAttemptTimeoutMs = configuration.PerAttemptTimeoutMs;
        revision.OverallCapMs = configuration.OverallCapMs;
        revision.RetryPolicy = configuration.RetryPolicy;
    }
}
