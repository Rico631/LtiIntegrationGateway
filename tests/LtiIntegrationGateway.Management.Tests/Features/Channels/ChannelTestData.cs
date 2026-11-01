using LtiIntegrationGateway.Features.Channels;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;

namespace LtiIntegrationGateway.Tests.Features.Channels;

internal static class ChannelTestData
{
    public const string ChannelName = "bank-a";

    public static ChannelConfiguration ValidConfiguration(string baseUrl = "https://bank-a.example") => new(
        BaseUrl: baseUrl,
        AuthType: ChannelAuthType.Bearer,
        ConfigKey: "Secrets:BankA:Token",
        HeaderName: null,
        UserName: null,
        PerAttemptTimeoutMs: 30_000,
        OverallCapMs: 120_000,
        RetryPolicy: new RetryPolicy([RetryRule.TransportError([1000, 2000, 4000])]));
}
