namespace LtiIntegrationGateway.Features.Outbound;

/// <summary>Коды отказа шлюза и транспортных ошибок в <c>OutboundResult</c>.</summary>
public static class OutboundErrorCodes
{
    public const string ProductNotAllowed = "PRODUCT_NOT_ALLOWED";
    public const string ChannelNotAllowed = "CHANNEL_NOT_ALLOWED";
    public const string ChannelNotActive = "CHANNEL_NOT_ACTIVE";
    public const string MethodNotAllowed = "METHOD_NOT_ALLOWED";
    public const string PathNotAllowed = "PATH_NOT_ALLOWED";
    public const string SecretNotConfigured = "SECRET_NOT_CONFIGURED";
    public const string AuthTypeUnsupported = "AUTH_TYPE_UNSUPPORTED";
    public const string Timeout = "TIMEOUT";
    public const string ConnectionFailed = "CONNECTION_FAILED";
    public const string TlsFailed = "TLS_FAILED";
    public const string RetriesExhausted = "RETRIES_EXHAUSTED";
}
