using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities;
using LtiIntegrationGateway.Infrastructure.Routing;
using LtiIntegrationGateway.Infrastructure.Security;

namespace LtiIntegrationGateway.Features.Inbound;

/// <summary>Результат проверки подлинности входящего вызова.</summary>
public enum InboundAuthenticationResult
{
    /// <summary>Вызов аутентифицирован либо для маршрута не требуется аутентификация.</summary>
    Allowed,

    /// <summary>Секрет не предъявлен или не совпал; ответ — 401 без тела.</summary>
    Unauthorized,

    /// <summary>Секрет маршрута не настроен в окружении; ответ — 503.</summary>
    SecretNotConfigured,
}

/// <summary>
/// Проверяет подлинность входящего вызова по настройкам маршрута. Сравнение секретов выполняется
/// за постоянное время, значения секретов не попадают в журнал и в ответы (ADR §13).
/// </summary>
public sealed class InboundRequestAuthenticator(ISecretStore secretStore, ILogger<InboundRequestAuthenticator> logger)
{
    /// <summary>Заголовок по умолчанию для типа авторизации api_key.</summary>
    public const string DefaultApiKeyHeader = "X-Api-Key";

    public InboundAuthenticationResult Authenticate(HttpContext context, InboundRouteMetadata route)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(route);

        return route.AuthType switch
        {
            ChannelAuthType.None => InboundAuthenticationResult.Allowed,
            ChannelAuthType.ApiKey => AuthenticateApiKey(context, route),
            ChannelAuthType.Basic => AuthenticateBasic(context, route),
            ChannelAuthType.ClientCertificate => AuthenticateClientCertificate(context, route),
            _ => Reject(route, "неподдерживаемый тип авторизации"),
        };
    }

    private InboundAuthenticationResult AuthenticateApiKey(HttpContext context, InboundRouteMetadata route)
    {
        var expected = ResolveSecret(route);

        if (expected is null)
        {
            return InboundAuthenticationResult.SecretNotConfigured;
        }

        var headerName = string.IsNullOrWhiteSpace(route.HeaderName) ? DefaultApiKeyHeader : route.HeaderName;
        var presented = context.Request.Headers[headerName].ToString();

        return FixedTimeEquals(presented, expected)
            ? InboundAuthenticationResult.Allowed
            : Reject(route, "несовпадение ключа");
    }

    private InboundAuthenticationResult AuthenticateBasic(HttpContext context, InboundRouteMetadata route)
    {
        var expected = ResolveSecret(route);

        if (expected is null)
        {
            return InboundAuthenticationResult.SecretNotConfigured;
        }

        if (!TryParseBasicCredentials(context.Request.Headers.Authorization.ToString(), out var userName, out var password))
        {
            return Reject(route, "учётные данные не предъявлены");
        }

        var userMatches = FixedTimeEquals(userName, route.UserName ?? string.Empty);
        var passwordMatches = FixedTimeEquals(password, expected);

        return userMatches && passwordMatches
            ? InboundAuthenticationResult.Allowed
            : Reject(route, "несовпадение учётных данных");
    }

    private InboundAuthenticationResult AuthenticateClientCertificate(HttpContext context, InboundRouteMetadata route) =>
        context.Connection.ClientCertificate is not null
            ? InboundAuthenticationResult.Allowed
            : Reject(route, "клиентский сертификат не предъявлен");

    private string? ResolveSecret(InboundRouteMetadata route)
    {
        if (string.IsNullOrWhiteSpace(route.ConfigKey))
        {
            return null;
        }

        var value = secretStore.Find(route.ConfigKey);

        if (string.IsNullOrEmpty(value))
        {
            logger.LogError(
                "Секрет для маршрута канала {Channel} не настроен: ключ конфигурации отсутствует или пуст",
                route.ChannelName);

            return null;
        }

        return value;
    }

    private InboundAuthenticationResult Reject(InboundRouteMetadata route, string reason)
    {
        logger.LogWarning(
            "Входящий вызов отклонён: канал {Channel}, тип авторизации {AuthType}, причина: {Reason}",
            route.ChannelName,
            route.AuthType,
            reason);

        return InboundAuthenticationResult.Unauthorized;
    }

    private static bool TryParseBasicCredentials(string header, out string userName, out string password)
    {
        userName = string.Empty;
        password = string.Empty;

        if (!AuthenticationHeaderValue.TryParse(header, out var value) ||
            !string.Equals(value.Scheme, "Basic", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrEmpty(value.Parameter))
        {
            return false;
        }

        string decoded;

        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(value.Parameter));
        }
        catch (FormatException)
        {
            return false;
        }

        var separator = decoded.IndexOf(':', StringComparison.Ordinal);

        if (separator < 0)
        {
            return false;
        }

        userName = decoded[..separator];
        password = decoded[(separator + 1)..];

        return true;
    }

    private static bool FixedTimeEquals(string presented, string expected)
    {
        var presentedBytes = Encoding.UTF8.GetBytes(presented);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);

        return presentedBytes.Length == expectedBytes.Length &&
            CryptographicOperations.FixedTimeEquals(presentedBytes, expectedBytes);
    }
}
