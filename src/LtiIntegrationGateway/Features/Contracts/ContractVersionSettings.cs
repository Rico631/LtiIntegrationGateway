using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Contracts;

namespace LtiIntegrationGateway.Features.Contracts;

/// <summary>Настройки входящих вызовов версии контракта.</summary>
public sealed record ContractVersionSettings(
    string? RecipientQueue,
    ChannelAuthType AuthType,
    string? ConfigKey,
    string? HeaderName,
    string? UserName,
    int? RateLimitPermit,
    int? RateLimitWindowSeconds)
{
    public static ContractVersionSettings Empty { get; } = new(
        RecipientQueue: null,
        AuthType: ChannelAuthType.None,
        ConfigKey: null,
        HeaderName: null,
        UserName: null,
        RateLimitPermit: null,
        RateLimitWindowSeconds: null);

    public static ContractVersionSettings From(ContractVersion version) => new(
        version.RecipientQueue,
        version.AuthType,
        version.ConfigKey,
        version.HeaderName,
        version.UserName,
        version.RateLimitPermit,
        version.RateLimitWindowSeconds);
}
