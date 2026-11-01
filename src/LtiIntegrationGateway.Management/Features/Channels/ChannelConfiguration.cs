using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;

namespace LtiIntegrationGateway.Features.Channels;

/// <summary>Настройки канала, которые фиксируются ревизией конфигурации.</summary>
public sealed record ChannelConfiguration(
    string BaseUrl,
    ChannelAuthType AuthType,
    string? ConfigKey,
    string? HeaderName,
    string? UserName,
    int PerAttemptTimeoutMs,
    int OverallCapMs,
    RetryPolicy RetryPolicy)
{
    public static ChannelConfiguration Empty { get; } = new(
        BaseUrl: string.Empty,
        AuthType: ChannelAuthType.None,
        ConfigKey: null,
        HeaderName: null,
        UserName: null,
        PerAttemptTimeoutMs: 0,
        OverallCapMs: 0,
        RetryPolicy: RetryPolicy.Empty);
}
