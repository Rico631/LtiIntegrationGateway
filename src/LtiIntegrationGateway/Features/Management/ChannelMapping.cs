using LtiIntegrationGateway.Contracts.Management.V1;
using LtiIntegrationGateway.Features.Channels;
using LtiIntegrationGateway.Features.Channels.GetChannel;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;
using DomainRetryRule = LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels.RetryRule;
using ProtoRetryRule = LtiIntegrationGateway.Contracts.Management.V1.RetryRule;

namespace LtiIntegrationGateway.Features.Management;

/// <summary>Отображение управляющих proto-сообщений в доменные настройки и обратно.</summary>
internal static class ChannelMapping
{
    private const string HttpStatusIn = "http_status_in";
    private const string TransportError = "transport_error";

    public static ChannelConfiguration ToConfiguration(
        ChannelSettings? settings,
        out IReadOnlyList<string> problems)
    {
        var list = new List<string>();
        problems = list;

        if (settings is null)
        {
            list.Add("settings: настройки обязательны");

            return ChannelConfiguration.Empty;
        }

        var rules = new List<DomainRetryRule>();

        foreach (var rule in settings.RetryPolicy)
        {
            RetryMatchType? match = rule.Match switch
            {
                HttpStatusIn => RetryMatchType.HttpStatusIn,
                TransportError => RetryMatchType.TransportError,
                _ => null,
            };

            if (match is null)
            {
                list.Add($"retry_policy: неизвестный признак '{rule.Match}'");

                continue;
            }

            rules.Add(new DomainRetryRule(match.Value, rule.StatusCodes.ToArray(), rule.DelaysMs.ToArray()));
        }

        return new ChannelConfiguration(
            settings.BaseUrl,
            ParseAuthType(settings.AuthType, list),
            NullIfEmpty(settings.ConfigKey),
            NullIfEmpty(settings.HeaderName),
            NullIfEmpty(settings.UserName),
            settings.PerAttemptTimeoutMs,
            settings.OverallCapMs,
            new RetryPolicy(rules));
    }

    public static ChannelSettings ToSettings(ChannelConfiguration configuration)
    {
        var settings = new ChannelSettings
        {
            BaseUrl = configuration.BaseUrl,
            AuthType = FormatAuthType(configuration.AuthType),
            ConfigKey = configuration.ConfigKey ?? string.Empty,
            HeaderName = configuration.HeaderName ?? string.Empty,
            UserName = configuration.UserName ?? string.Empty,
            PerAttemptTimeoutMs = configuration.PerAttemptTimeoutMs,
            OverallCapMs = configuration.OverallCapMs,
        };

        foreach (var rule in configuration.RetryPolicy.Rules)
        {
            settings.RetryPolicy.Add(new ProtoRetryRule
            {
                Match = rule.Match == RetryMatchType.HttpStatusIn ? HttpStatusIn : TransportError,
                StatusCodes = { rule.StatusCodes },
                DelaysMs = { rule.DelaysMs },
            });
        }

        return settings;
    }

    public static ChannelInfo ToInfo(ChannelDetails details)
    {
        var info = new ChannelInfo
        {
            ChannelId = details.Id.ToString(),
            Name = details.Name,
            ActiveSettings = details.ActiveRevision is null
                ? null
                : ToSettings(details.ActiveRevision.Configuration),
        };

        foreach (var revision in details.Revisions)
        {
            info.Revisions.Add(new ChannelRevisionInfo
            {
                Number = revision.Number,
                Status = revision.Status.ToString(),
            });
        }

        return info;
    }

    public static ChannelAuthType ParseAuthType(string? value, List<string> problems)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();

        switch (normalized)
        {
            case "" or "none":
                return ChannelAuthType.None;
            case "bearer":
                return ChannelAuthType.Bearer;
            case "api_key":
                return ChannelAuthType.ApiKey;
            case "basic":
                return ChannelAuthType.Basic;
            case "client_certificate":
                return ChannelAuthType.ClientCertificate;
            default:
                problems.Add($"auth_type: тип '{value}' не поддерживается");

                return ChannelAuthType.None;
        }
    }

    public static string FormatAuthType(ChannelAuthType authType) => authType switch
    {
        ChannelAuthType.Bearer => "bearer",
        ChannelAuthType.ApiKey => "api_key",
        ChannelAuthType.Basic => "basic",
        ChannelAuthType.ClientCertificate => "client_certificate",
        _ => "none",
    };

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
