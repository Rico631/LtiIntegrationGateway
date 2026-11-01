using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace LtiIntegrationGateway.Infrastructure.Security;

/// <summary>
/// Клиентские сертификаты исходящих каналов из <see cref="IConfiguration"/>: по ключу ревизии
/// задаётся путь к PFX-файлу либо PFX в base64, пароль — по ключу <c>&lt;config_key&gt;:Password</c>.
/// </summary>
/// <remarks>
/// Материал загружается с <see cref="X509KeyStorageFlags.EphemeralKeySet"/>, поэтому приватный ключ
/// не попадает в хранилище ОС. Загруженный сертификат кэшируется по ключу конфигурации на время
/// жизни приложения: PFX разбирается один раз, а смена материала вступает в силу только после
/// перезапуска (ADR §7.4). Хранилище владеет сертификатами и освобождает их при остановке
/// приложения. Значения материала и пароля не возвращаются в сообщениях об ошибках.
/// </remarks>
public sealed class ConfigurationChannelCertificateStore(
    IConfiguration configuration,
    TimeProvider timeProvider) : IChannelCertificateStore, IDisposable
{
    /// <summary>Суффикс ключа конфигурации, по которому читается пароль PFX.</summary>
    public const string PasswordSuffix = ":Password";

    private readonly ConcurrentDictionary<string, X509Certificate2> _certificates = new(StringComparer.Ordinal);
    private int _disposed;

    public ChannelCertificateResult Load(string? configKey)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);

        if (string.IsNullOrWhiteSpace(configKey))
        {
            return ChannelCertificateResult.Failed("ключ конфигурации сертификата не задан");
        }

        if (_certificates.TryGetValue(configKey, out var cached))
        {
            var problem = DescribeValidityProblem(cached);

            return problem is null
                ? ChannelCertificateResult.Loaded(cached)
                : ChannelCertificateResult.Failed(problem);
        }

        var value = configuration[configKey];

        if (string.IsNullOrWhiteSpace(value))
        {
            return ChannelCertificateResult.Failed("материал сертификата не настроен");
        }

        var password = configuration[configKey + PasswordSuffix];

        if (!TryReadMaterial(value, out var material))
        {
            return ChannelCertificateResult.Failed("материал сертификата не удалось прочитать");
        }

        var loaded = LoadFromPkcs12(material, password);

        if (loaded.Certificate is null)
        {
            return loaded;
        }

        var stored = _certificates.GetOrAdd(configKey, loaded.Certificate);

        if (!ReferenceEquals(stored, loaded.Certificate))
        {
            // Параллельный вызов уже загрузил сертификат для этого ключа.
            loaded.Certificate.Dispose();
        }

        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);

        return ChannelCertificateResult.Loaded(stored);
    }

    public ChannelCertificateInfo GetInfo(string? configKey)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);

        if (string.IsNullOrWhiteSpace(configKey))
        {
            return ChannelCertificateInfo.Missing("ключ конфигурации сертификата не задан");
        }

        if (_certificates.TryGetValue(configKey, out var cached))
        {
            // Закэшированный сертификат принадлежит хранилищу: только читаем срок и валидность.
            return Describe(cached);
        }

        var value = configuration[configKey];

        if (string.IsNullOrWhiteSpace(value))
        {
            return ChannelCertificateInfo.Missing("материал сертификата не настроен");
        }

        var password = configuration[configKey + PasswordSuffix];

        if (!TryReadMaterial(value, out var material))
        {
            return ChannelCertificateInfo.Missing("материал сертификата не удалось прочитать");
        }

        var certificate = TryLoadCertificate(material, password);

        if (certificate is null)
        {
            return ChannelCertificateInfo.Missing(
                "материал сертификата не удалось загрузить как PFX или пароль неверен");
        }

        // Диагностический экземпляр не кэшируется: он нужен только для чтения срока и освобождается.
        using (certificate)
        {
            return Describe(certificate);
        }
    }

    /// <summary>Освобождает все закэшированные сертификаты при остановке приложения.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        foreach (var pair in _certificates)
        {
            if (_certificates.TryRemove(pair))
            {
                pair.Value.Dispose();
            }
        }
    }

    /// <summary>
    /// Значение трактуется как путь к файлу, если файл существует, иначе — как PFX в base64.
    /// </summary>
    private static bool TryReadMaterial(string value, out byte[] material)
    {
        material = [];

        try
        {
            if (File.Exists(value))
            {
                material = File.ReadAllBytes(value);

                return true;
            }

            material = Convert.FromBase64String(value);

            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or FormatException or ArgumentException or NotSupportedException)
        {
            material = [];

            return false;
        }
    }

    private ChannelCertificateResult LoadFromPkcs12(byte[] material, string? password)
    {
        var certificate = TryLoadCertificate(material, password);

        if (certificate is null)
        {
            return ChannelCertificateResult.Failed(
                "материал сертификата не удалось загрузить как PFX или пароль неверен");
        }

        if (!certificate.HasPrivateKey)
        {
            certificate.Dispose();

            return ChannelCertificateResult.Failed("у сертификата нет приватного ключа");
        }

        var problem = DescribeValidityProblem(certificate);

        if (problem is not null)
        {
            certificate.Dispose();

            return ChannelCertificateResult.Failed(problem);
        }

        return ChannelCertificateResult.Loaded(certificate);
    }

    /// <summary>Разбирает PFX; <c>null</c> — материал не читается или пароль неверен.</summary>
    private static X509Certificate2? TryLoadCertificate(byte[] material, string? password)
    {
        try
        {
            return X509CertificateLoader.LoadPkcs12(
                material,
                password,
                X509KeyStorageFlags.EphemeralKeySet);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    /// <summary>
    /// Проверяет срок действия по текущему времени: выполняется при загрузке и при каждом обращении
    /// к закэшированному сертификату, поэтому истёкший материал не используется.
    /// </summary>
    private string? DescribeValidityProblem(X509Certificate2 certificate)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (certificate.NotAfter.ToUniversalTime() <= now)
        {
            return "срок действия сертификата истёк";
        }

        return certificate.NotBefore.ToUniversalTime() > now
            ? "срок действия сертификата ещё не наступил"
            : null;
    }

    /// <summary>
    /// Диагностические сведения без требования валидности: срок возвращается и для истёкшего, и для
    /// ещё не вступившего в силу сертификата. Вызывающий владеет экземпляром и освобождает его сам.
    /// </summary>
    private ChannelCertificateInfo Describe(X509Certificate2 certificate)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var notBefore = certificate.NotBefore.ToUniversalTime();
        var notAfter = certificate.NotAfter.ToUniversalTime();
        var expiresAt = new DateTimeOffset(notAfter, TimeSpan.Zero);

        if (!certificate.HasPrivateKey)
        {
            return ChannelCertificateInfo.Parsed(
                expiresAt,
                false,
                "у сертификата нет приватного ключа");
        }

        if (notAfter <= now)
        {
            return ChannelCertificateInfo.Parsed(
                expiresAt,
                false,
                "срок действия сертификата истёк");
        }

        return notBefore > now
            ? ChannelCertificateInfo.Parsed(
                expiresAt,
                false,
                "срок действия сертификата ещё не наступил")
            : ChannelCertificateInfo.Parsed(expiresAt, true, problem: null);
    }
}
