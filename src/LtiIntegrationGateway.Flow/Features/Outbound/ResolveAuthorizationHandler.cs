using System.Security.Cryptography.X509Certificates;
using System.Text;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;
using LtiIntegrationGateway.Infrastructure.Results;
using LtiIntegrationGateway.Infrastructure.Security;

namespace LtiIntegrationGateway.Features.Outbound;

/// <summary>Данные авторизации исходящего вызова: заголовки, query-параметры и клиентский сертификат.</summary>
public sealed record AuthorizationData(
    IReadOnlyDictionary<string, string> Headers,
    IReadOnlyList<(string Name, string Value)> QueryParameters,
    X509Certificate2? ClientCertificate = null);

public sealed class ResolveAuthorizationHandler(
    ISecretStore secretStore,
    IChannelCertificateStore certificateStore)
{
    public Result<AuthorizationData> Handle(ActiveChannel channel)
    {
        switch (channel.AuthType)
        {
            case ChannelAuthType.None:
                return Success();

            case ChannelAuthType.ClientCertificate:
                {
                    var loaded = certificateStore.Load(channel.ConfigKey);

                    return loaded.Certificate is null
                        ? Failure(
                            OutboundErrorCodes.SecretNotConfigured,
                            SecretMissingMessage(channel.ConfigKey, loaded.Error))
                        : Success(certificate: loaded.Certificate);
                }

            case ChannelAuthType.Bearer:
                {
                    var token = secretStore.Find(channel.ConfigKey ?? string.Empty);

                    return token is null
                        ? Failure(OutboundErrorCodes.SecretNotConfigured, SecretMissingMessage(channel.ConfigKey))
                        : Success(new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["Authorization"] = $"Bearer {token}",
                        });
                }

            case ChannelAuthType.ApiKey:
                {
                    var value = secretStore.Find(channel.ConfigKey ?? string.Empty);

                    if (value is null)
                    {
                        return Failure(OutboundErrorCodes.SecretNotConfigured, SecretMissingMessage(channel.ConfigKey));
                    }

                    return string.IsNullOrWhiteSpace(channel.HeaderName)
                        ? Success(queryParameters: [("api_key", value)])
                        : Success(new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            [channel.HeaderName] = value,
                        });
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

                    return Success(new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["Authorization"] = $"Basic {credentials}",
                    });
                }

            default:
                return Failure(
                    OutboundErrorCodes.AuthTypeUnsupported,
                    "тип авторизации канала не поддерживается");
        }
    }

    private static Result<AuthorizationData> Success(
        IReadOnlyDictionary<string, string>? headers = null,
        IReadOnlyList<(string Name, string Value)>? queryParameters = null,
        X509Certificate2? certificate = null) =>
        Result.Success(new AuthorizationData(
            headers ?? new Dictionary<string, string>(StringComparer.Ordinal),
            queryParameters ?? [],
            certificate));

    private static Result<AuthorizationData> Failure(string code, string message) =>
        Result.Failure<AuthorizationData>(code, message);

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
