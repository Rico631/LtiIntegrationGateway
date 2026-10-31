using System.Security.Cryptography.X509Certificates;
using System.Text;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities;
using LtiIntegrationGateway.Infrastructure.Security;

namespace LtiIntegrationGateway.Features.Outbound;

/// <summary>
/// Результат подстановки авторизации: заголовки, дополнительные query-параметры, клиентский
/// сертификат для TLS-рукопожатия или код отказа.
/// </summary>
public sealed record AuthorizationResult(
    IReadOnlyDictionary<string, string> Headers,
    IReadOnlyList<(string Name, string Value)> QueryParameters,
    string? ErrorCode,
    string? ErrorMessage,
    X509Certificate2? ClientCertificate = null);

public sealed class ResolveAuthorizationHandler(
    ISecretStore secretStore,
    IChannelCertificateStore certificateStore)
{
    public AuthorizationResult Handle(ActiveChannel channel)
    {
        switch (channel.AuthType)
        {
            case ChannelAuthType.None:
                return Empty();

            case ChannelAuthType.ClientCertificate:
            {
                var loaded = certificateStore.Load(channel.ConfigKey);

                if (loaded.Certificate is null)
                {
                    return Failure(
                        OutboundErrorCodes.SecretNotConfigured,
                        SecretMissingMessage(channel.ConfigKey, loaded.Error));
                }

                return new AuthorizationResult(
                    new Dictionary<string, string>(StringComparer.Ordinal),
                    [],
                    null,
                    null,
                    loaded.Certificate);
            }

            case ChannelAuthType.Bearer:
            {
                var token = secretStore.Find(channel.ConfigKey ?? string.Empty);

                return token is null
                    ? Failure(OutboundErrorCodes.SecretNotConfigured, SecretMissingMessage(channel.ConfigKey))
                    : new AuthorizationResult(
                        new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["Authorization"] = $"Bearer {token}",
                        },
                        [],
                        null,
                        null);
            }

            case ChannelAuthType.ApiKey:
            {
                var value = secretStore.Find(channel.ConfigKey ?? string.Empty);

                if (value is null)
                {
                    return Failure(OutboundErrorCodes.SecretNotConfigured, SecretMissingMessage(channel.ConfigKey));
                }

                if (string.IsNullOrWhiteSpace(channel.HeaderName))
                {
                    return new AuthorizationResult(
                        new Dictionary<string, string>(StringComparer.Ordinal),
                        [("api_key", value)],
                        null,
                        null);
                }

                return new AuthorizationResult(
                    new Dictionary<string, string>(StringComparer.Ordinal) { [channel.HeaderName] = value },
                    [],
                    null,
                    null);
            }

            case ChannelAuthType.Basic:
            {
                var password = secretStore.Find(channel.ConfigKey ?? string.Empty);

                if (password is null)
                {
                    return Failure(OutboundErrorCodes.SecretNotConfigured, SecretMissingMessage(channel.ConfigKey));
                }

                var credentials = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"{channel.UserName}:{password}"));

                return new AuthorizationResult(
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["Authorization"] = $"Basic {credentials}",
                    },
                    [],
                    null,
                    null);
            }

            default:
                return Failure(
                    OutboundErrorCodes.AuthTypeUnsupported,
                    "тип авторизации канала не поддерживается");
        }
    }

    private static AuthorizationResult Empty() =>
        new(new Dictionary<string, string>(StringComparer.Ordinal), [], null, null);

    private static AuthorizationResult Failure(string code, string message) =>
        new(new Dictionary<string, string>(StringComparer.Ordinal), [], code, message);

    /// <summary>
    /// Диагностика отсутствующего или непригодного секрета в формате ADR §7.5: только имя ключа
    /// конфигурации и, при необходимости, причина; значение секрета в текст не попадает.
    /// </summary>
    private static string SecretMissingMessage(string? configKey, string? reason = null)
    {
        var message = $"secret key missing (key: {configKey})";

        return string.IsNullOrWhiteSpace(reason) ? message : $"{message}: {reason}";
    }
}
