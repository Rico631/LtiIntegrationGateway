using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;

namespace LtiIntegrationGateway.Infrastructure.Http;

/// <summary>
/// Ключ изоляции HTTP-соединений исходящего канала: базовый адрес, тип авторизации и отпечаток
/// клиентского сертификата (ADR §7.7).
/// </summary>
/// <param name="BaseUrl">Базовый адрес канала без завершающего символа '/'.</param>
/// <param name="AuthType">Тип авторизации ревизии: клиентский сертификат меняет TLS-настройки.</param>
/// <param name="CertificateThumbprint">
/// SHA-256 отпечаток клиентского сертификата либо <c>null</c>, если сертификат не используется.
/// </param>
public sealed record ChannelHttpClientKey(
    string BaseUrl,
    ChannelAuthType AuthType,
    string? CertificateThumbprint)
{
    /// <summary>
    /// Строит ключ по настройкам активной ревизии и загруженному сертификату. Сертификат в ключ не
    /// попадает: для изоляции достаточно отпечатка SHA-256 по <see cref="X509Certificate2.RawData"/>.
    /// </summary>
    public static ChannelHttpClientKey From(
        string baseUrl,
        ChannelAuthType authType,
        X509Certificate2? clientCertificate) =>
        new(NormalizeBaseUrl(baseUrl), authType, Thumbprint(clientCertificate));

    private static string NormalizeBaseUrl(string baseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);

        return baseUrl.TrimEnd('/');
    }

    private static string? Thumbprint(X509Certificate2? certificate) =>
        certificate?.GetCertHashString(HashAlgorithmName.SHA256);
}
