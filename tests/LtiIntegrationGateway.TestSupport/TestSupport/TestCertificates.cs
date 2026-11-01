using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace LtiIntegrationGateway.Tests.TestSupport;

/// <summary>
/// Самоподписанные клиентские сертификаты для тестов mTLS: создаются в памяти, PFX пишется во
/// временный файл только по запросу теста и удаляется им же.
/// </summary>
public static class TestCertificates
{
    /// <summary>Пароль PFX, используемый тестами; значение не является секретом эксплуатации.</summary>
    public const string Password = "test-pfx-password-1";

    /// <summary>Создаёт самоподписанный сертификат клиента с приватным ключом RSA.</summary>
    public static X509Certificate2 CreateSelfSigned(
        string subject = "CN=lti-test-client",
        DateTimeOffset? notBefore = null,
        DateTimeOffset? notAfter = null)
    {
        using var rsa = RSA.Create(2048);

        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: false));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.2") },
                critical: false));

        return request.CreateSelfSigned(
            notBefore ?? DateTimeOffset.UtcNow.AddDays(-1),
            notAfter ?? DateTimeOffset.UtcNow.AddDays(1));
    }

    /// <summary>Экспортирует сертификат с приватным ключом в PFX.</summary>
    public static byte[] ExportPfx(X509Certificate2 certificate, string? password) =>
        certificate.Export(X509ContentType.Pfx, password);

    /// <summary>Экспортирует PFX, содержащий сертификат без приватного ключа.</summary>
    public static byte[] ExportPfxWithoutPrivateKey(X509Certificate2 certificate)
    {
        using var publicOnly = X509CertificateLoader.LoadCertificate(certificate.RawData);

        return publicOnly.Export(X509ContentType.Pfx);
    }

    /// <summary>Пишет PFX во временный файл и возвращает путь; тест обязан удалить файл.</summary>
    public static string WriteTemporaryPfx(byte[] pfx)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lti-gateway-test-{Guid.NewGuid():N}.pfx");

        File.WriteAllBytes(path, pfx);

        return path;
    }
}
