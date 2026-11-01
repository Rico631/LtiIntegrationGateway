using System.Security.Authentication;
using System.Security.Cryptography;
using LtiIntegrationGateway.Infrastructure.Http;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;
using LtiIntegrationGateway.Tests.TestSupport;

namespace LtiIntegrationGateway.Tests.Infrastructure.Http;

public class ChannelHttpClientPoolTests
{
    private const string BaseUrl = "https://bank-a.example";

    [Test]
    public async Task Same_key_returns_same_handler()
    {
        using var pool = new ChannelHttpClientPool(TimeProvider.System);
        var key = ChannelHttpClientKey.From(BaseUrl, ChannelAuthType.Bearer, null);

        using var first = pool.Acquire(key, null);
        using var second = pool.Acquire(key, null);

        await Assert.That(ReferenceEquals(first.Handler, second.Handler)).IsTrue();
    }

    [Test]
    public async Task Different_certificates_return_different_handlers()
    {
        using var pool = new ChannelHttpClientPool(TimeProvider.System);
        using var certificateA = TestCertificates.CreateSelfSigned("CN=client-a");
        using var certificateB = TestCertificates.CreateSelfSigned("CN=client-b");

        var keyA = ChannelHttpClientKey.From(BaseUrl, ChannelAuthType.ClientCertificate, certificateA);
        var keyB = ChannelHttpClientKey.From(BaseUrl, ChannelAuthType.ClientCertificate, certificateB);

        await Assert.That(keyA).IsNotEqualTo(keyB);

        using var leaseA = pool.Acquire(keyA, certificateA);
        using var leaseB = pool.Acquire(keyB, certificateB);

        await Assert.That(ReferenceEquals(leaseA.Handler, leaseB.Handler)).IsFalse();

        var certificatesA = ((SocketsHttpHandler)leaseA.Handler).SslOptions.ClientCertificates;
        var certificatesB = ((SocketsHttpHandler)leaseB.Handler).SslOptions.ClientCertificates;

        await Assert.That(certificatesA).IsNotNull();
        await Assert.That(certificatesA!.Count).IsEqualTo(1);
        await Assert.That(certificatesA[0]!.GetCertHashString(HashAlgorithmName.SHA256))
            .IsEqualTo(certificateA.GetCertHashString(HashAlgorithmName.SHA256));

        await Assert.That(certificatesB).IsNotNull();
        await Assert.That(certificatesB!.Count).IsEqualTo(1);
        await Assert.That(certificatesB[0]!.GetCertHashString(HashAlgorithmName.SHA256))
            .IsEqualTo(certificateB.GetCertHashString(HashAlgorithmName.SHA256));
    }

    [Test]
    public async Task Key_differs_by_auth_type_and_ignores_trailing_slash()
    {
        using var pool = new ChannelHttpClientPool(TimeProvider.System);

        var withSlash = ChannelHttpClientKey.From(BaseUrl + "/", ChannelAuthType.None, null);
        var withoutSlash = ChannelHttpClientKey.From(BaseUrl, ChannelAuthType.None, null);

        await Assert.That(withSlash).IsEqualTo(withoutSlash);

        using var plain = pool.Acquire(withoutSlash, null);
        using var bearer = pool.Acquire(
            ChannelHttpClientKey.From(BaseUrl, ChannelAuthType.Bearer, null),
            null);

        await Assert.That(ReferenceEquals(plain.Handler, bearer.Handler)).IsFalse();
    }

    [Test]
    public async Task Expired_entry_is_recreated()
    {
        var time = new ManualTimeProvider();
        using var pool = new ChannelHttpClientPool(time);
        var key = ChannelHttpClientKey.From(BaseUrl, ChannelAuthType.Bearer, null);

        HttpMessageHandler firstHandler;

        using (var first = pool.Acquire(key, null))
        {
            firstHandler = first.Handler;
        }

        // Меньше CleanupInterval: обход словаря пропущен, запись переиспользуется.
        time.Advance(TimeSpan.FromSeconds(10));

        using (var withinCleanupInterval = pool.Acquire(key, null))
        {
            await Assert.That(ReferenceEquals(firstHandler, withinCleanupInterval.Handler)).IsTrue();
        }

        time.Advance(ChannelHttpClientPool.EntryLifetime - TimeSpan.FromMinutes(1));

        using (var withinLifetime = pool.Acquire(key, null))
        {
            await Assert.That(ReferenceEquals(firstHandler, withinLifetime.Handler)).IsTrue();
        }

        // Срок жизни истёк и интервал уборки прошёл: создаётся новый обработчик.
        time.Advance(TimeSpan.FromMinutes(2));

        using var afterLifetime = pool.Acquire(key, null);

        await Assert.That(ReferenceEquals(firstHandler, afterLifetime.Handler)).IsFalse();
    }

    [Test]
    public async Task Key_with_thumbprint_requires_certificate()
    {
        using var pool = new ChannelHttpClientPool(TimeProvider.System);
        using var certificate = TestCertificates.CreateSelfSigned();
        var key = ChannelHttpClientKey.From(BaseUrl, ChannelAuthType.ClientCertificate, certificate);

        Exception? captured = null;

        try
        {
            using var lease = pool.Acquire(key, null);
        }
        catch (Exception exception)
        {
            captured = exception;
        }

        await Assert.That(captured).IsNotNull();
        await Assert.That(captured is InvalidOperationException).IsTrue();
    }

    [Test]
    public async Task Acquire_racing_with_dispose_does_not_lease_handler()
    {
        var time = new ManualTimeProvider();
        using var pool = new ChannelHttpClientPool(time);
        var key = ChannelHttpClientKey.From(BaseUrl, ChannelAuthType.Bearer, null);
        var timeCalls = 0;

        // Второе обращение к времени выполняется уже внутри цикла Acquire, перед добавлением записи:
        // так детерминированно воспроизводится гонка «пул остановлен после проверки _disposed».
        time.OnGetUtcNow = () =>
        {
            timeCalls++;

            if (timeCalls == 2)
            {
                pool.Dispose();
            }
        };

        Exception? captured = null;

        try
        {
            using var lease = pool.Acquire(key, null);
        }
        catch (Exception exception)
        {
            captured = exception;
        }

        await Assert.That(captured).IsNotNull();
        await Assert.That(captured is ObjectDisposedException).IsTrue();
    }

    [Test]
    public async Task Handler_has_expected_connection_and_tls_settings()
    {
        using var pool = new ChannelHttpClientPool(TimeProvider.System);
        using var certificate = TestCertificates.CreateSelfSigned();
        var key = ChannelHttpClientKey.From(BaseUrl, ChannelAuthType.ClientCertificate, certificate);

        using var lease = pool.Acquire(key, certificate);
        var handler = (SocketsHttpHandler)lease.Handler;

        await Assert.That(handler.AllowAutoRedirect).IsFalse();
        await Assert.That(handler.PooledConnectionLifetime).IsEqualTo(TimeSpan.FromMinutes(5));
        await Assert.That(handler.SslOptions.EnabledSslProtocols)
            .IsEqualTo(SslProtocols.Tls12 | SslProtocols.Tls13);
        await Assert.That(handler.SslOptions.RemoteCertificateValidationCallback).IsNull();
        await Assert.That(handler.SslOptions.ClientCertificates).IsNotNull();
        await Assert.That(handler.SslOptions.ClientCertificates!.Count).IsEqualTo(1);
        await Assert.That(handler.SslOptions.ClientCertificates[0]!.GetCertHashString(HashAlgorithmName.SHA256))
            .IsEqualTo(certificate.GetCertHashString(HashAlgorithmName.SHA256));
    }
}
