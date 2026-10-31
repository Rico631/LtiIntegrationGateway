using LtiIntegrationGateway.Features.Channels;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;

namespace LtiIntegrationGateway.Tests.Features.Channels;

public class ChannelConfigurationValidatorTests
{
    private static readonly ChannelConfigurationValidator Validator = new();

    private static ChannelConfiguration With(
        string baseUrl = "https://bank-a.example",
        ChannelAuthType authType = ChannelAuthType.Bearer,
        string? configKey = "Secrets:BankA:Token",
        RetryPolicy? policy = null,
        int perAttemptTimeoutMs = 30_000,
        int overallCapMs = 120_000) =>
        new(
            baseUrl,
            authType,
            configKey,
            HeaderName: null,
            UserName: null,
            perAttemptTimeoutMs,
            overallCapMs,
            policy ?? new RetryPolicy([RetryRule.TransportError([1000])]));

    [Test]
    public async Task Valid_configuration_has_no_problems()
    {
        var problems = Validator.Validate(ChannelTestData.ValidConfiguration());

        await Assert.That(string.Join("; ", problems)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Http_base_url_is_rejected()
    {
        var problems = Validator.Validate(With(baseUrl: "http://bank-a.example"));

        await Assert.That(problems.Any(problem => problem.StartsWith("base_url", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Base_url_with_user_info_is_rejected()
    {
        var problems = Validator.Validate(With(baseUrl: "https://user:pass@bank-a.example"));

        await Assert.That(problems.Any(problem => problem.Contains("учётные данные", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Missing_config_key_is_rejected_for_secret_auth()
    {
        var problems = Validator.Validate(With(configKey: null));

        await Assert.That(problems.Any(problem => problem.StartsWith("config_key", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Delay_order_is_preserved_in_policy()
    {
        var policy = new RetryPolicy([RetryRule.TransportError([1000, 2000, 4000])]);

        var problems = Validator.Validate(With(policy: policy));

        await Assert.That(problems.Count).IsEqualTo(0);
        await Assert.That(policy.Rules[0].DelaysMs.SequenceEqual([1000, 2000, 4000])).IsTrue();
        await Assert.That(policy.Rules[0].DelaysMs.Count).IsEqualTo(3);
    }

    [Test]
    public async Task Six_delays_are_rejected()
    {
        var policy = new RetryPolicy([RetryRule.TransportError([1000, 2000, 3000, 4000, 5000, 6000])]);

        var problems = Validator.Validate(With(policy: policy));

        await Assert.That(problems.Any(problem => problem.Contains(".delays", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Empty_delays_are_rejected()
    {
        var policy = new RetryPolicy([RetryRule.TransportError([])]);

        var problems = Validator.Validate(With(policy: policy));

        await Assert.That(problems.Any(problem => problem.Contains(".delays", StringComparison.Ordinal))).IsTrue();
    }
}
