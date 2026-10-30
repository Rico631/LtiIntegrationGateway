using LtiIntegrationGateway.Infrastructure.Persistence.Entities;

namespace LtiIntegrationGateway.Features.Channels;

/// <summary>Перенос настроек в сущность ревизии и обратно.</summary>
internal static class ChannelRevisionWriter
{
    public static ChannelRevision CreateDraft(Guid channelId, int number, ChannelConfiguration configuration)
    {
        var revision = new ChannelRevision
        {
            Id = Guid.NewGuid(),
            ChannelId = channelId,
            Number = number,
            Status = ChannelRevisionStatus.Draft,
            BaseUrl = configuration.BaseUrl,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        Apply(revision, configuration);

        return revision;
    }

    public static void Apply(ChannelRevision revision, ChannelConfiguration configuration)
    {
        revision.BaseUrl = configuration.BaseUrl;
        revision.AuthType = configuration.AuthType;
        revision.ConfigKey = configuration.ConfigKey;
        revision.HeaderName = configuration.HeaderName;
        revision.UserName = configuration.UserName;
        revision.PerAttemptTimeoutMs = configuration.PerAttemptTimeoutMs;
        revision.OverallCapMs = configuration.OverallCapMs;
        revision.RetryPolicy = configuration.RetryPolicy;

        revision.AllowedMethods.Clear();
        revision.AllowedPaths.Clear();

        foreach (var method in configuration.AllowedMethods.Distinct(StringComparer.Ordinal))
        {
            revision.AllowedMethods.Add(new ChannelAllowedMethod { Id = Guid.NewGuid(), Method = method });
        }

        foreach (var template in configuration.AllowedPaths.Distinct(StringComparer.Ordinal))
        {
            revision.AllowedPaths.Add(new ChannelAllowedPath { Id = Guid.NewGuid(), Template = template });
        }
    }

    public static ChannelConfiguration Read(ChannelRevision revision) => new(
        revision.BaseUrl,
        revision.AuthType,
        revision.ConfigKey,
        revision.HeaderName,
        revision.UserName,
        revision.PerAttemptTimeoutMs,
        revision.OverallCapMs,
        revision.RetryPolicy,
        revision.AllowedMethods.Select(method => method.Method).ToArray(),
        revision.AllowedPaths.Select(path => path.Template).ToArray());
}
