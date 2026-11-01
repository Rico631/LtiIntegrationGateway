using LtiIntegrationGateway.Infrastructure.Security;
using LtiIntegrationGateway.Tests.TestSupport;
using Microsoft.Extensions.Configuration;

namespace LtiIntegrationGateway.Tests.Infrastructure.Security;

public class ChannelCertificateStoreTests
{
    private const string ConfigKey = "Channels:BankA:ClientCertificate";
    private const string WrongPassword = "wrong-pfx-password-2";

    private static ConfigurationChannelCertificateStore CreateStore(
        IReadOnlyDictionary<string, string?> settings,
        TimeProvider? timeProvider = null) =>
        new(
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
            timeProvider ?? TimeProvider.System);

    [Test]
    public async Task Pfx_file_with_password_is_loaded_with_private_key()
    {
        using var certificate = TestCertificates.CreateSelfSigned();
        var path = TestCertificates.WriteTemporaryPfx(
            TestCertificates.ExportPfx(certificate, TestCertificates.Password));

        try
        {
            using var store = CreateStore(new Dictionary<string, string?>
            {
                [ConfigKey] = path,
                [ConfigKey + ConfigurationChannelCertificateStore.PasswordSuffix] = TestCertificates.Password,
            });

            var result = store.Load(ConfigKey);

            await Assert.That(result.Error).IsNull();
            await Assert.That(result.Certificate).IsNotNull();
            await Assert.That(result.Certificate!.HasPrivateKey).IsTrue();
            await Assert.That(result.Certificate.Thumbprint).IsEqualTo(certificate.Thumbprint);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task Base64_pfx_is_loaded_with_private_key()
    {
        using var certificate = TestCertificates.CreateSelfSigned("CN=lti-test-base64");
        var material = Convert.ToBase64String(
            TestCertificates.ExportPfx(certificate, TestCertificates.Password));

        using var store = CreateStore(new Dictionary<string, string?>
        {
            [ConfigKey] = material,
            [ConfigKey + ConfigurationChannelCertificateStore.PasswordSuffix] = TestCertificates.Password,
        });

        var result = store.Load(ConfigKey);

        await Assert.That(result.Error).IsNull();
        await Assert.That(result.Certificate).IsNotNull();
        await Assert.That(result.Certificate!.HasPrivateKey).IsTrue();
        await Assert.That(result.Certificate.Subject).IsEqualTo(certificate.Subject);
    }

    [Test]
    public async Task Pfx_without_password_is_loaded_with_private_key()
    {
        using var certificate = TestCertificates.CreateSelfSigned("CN=lti-test-unprotected");
        var material = Convert.ToBase64String(TestCertificates.ExportPfx(certificate, password: null));

        using var store = CreateStore(new Dictionary<string, string?>
        {
            [ConfigKey] = material,
        });

        var result = store.Load(ConfigKey);

        await Assert.That(result.Error).IsNull();
        await Assert.That(result.Certificate).IsNotNull();
        await Assert.That(result.Certificate!.HasPrivateKey).IsTrue();
    }
    [Test]
    public async Task Same_key_returns_cached_certificate_instance()
    {
        using var certificate = TestCertificates.CreateSelfSigned("CN=lti-test-cache");
        var material = Convert.ToBase64String(
            TestCertificates.ExportPfx(certificate, TestCertificates.Password));

        using var store = CreateStore(new Dictionary<string, string?>
        {
            [ConfigKey] = material,
            [ConfigKey + ConfigurationChannelCertificateStore.PasswordSuffix] = TestCertificates.Password,
        });

        var first = store.Load(ConfigKey);
        var second = store.Load(ConfigKey);

        await Assert.That(first.Certificate).IsNotNull();
        await Assert.That(second.Certificate).IsNotNull();
        await Assert.That(ReferenceEquals(first.Certificate, second.Certificate)).IsTrue();
    }

    [Test]
    public async Task Disposed_store_rejects_load()
    {
        using var certificate = TestCertificates.CreateSelfSigned("CN=lti-test-disposed");
        var material = Convert.ToBase64String(
            TestCertificates.ExportPfx(certificate, TestCertificates.Password));

        var store = CreateStore(new Dictionary<string, string?>
        {
            [ConfigKey] = material,
            [ConfigKey + ConfigurationChannelCertificateStore.PasswordSuffix] = TestCertificates.Password,
        });

        var loaded = store.Load(ConfigKey);

        await Assert.That(loaded.Certificate).IsNotNull();

        store.Dispose();

        Exception? captured = null;

        try
        {
            store.Load(ConfigKey);
        }
        catch (Exception exception)
        {
            captured = exception;
        }

        await Assert.That(captured).IsNotNull();
        await Assert.That(captured is ObjectDisposedException).IsTrue();
    }



    [Test]
    public async Task Cached_certificate_is_revalidated_against_current_time()
    {
        var time = new ManualTimeProvider { UtcNow = DateTimeOffset.UtcNow };

        using var certificate = TestCertificates.CreateSelfSigned(
            "CN=lti-test-cache-expiry",
            notBefore: time.UtcNow.AddDays(-1),
            notAfter: time.UtcNow.AddDays(1));

        var material = Convert.ToBase64String(
            TestCertificates.ExportPfx(certificate, TestCertificates.Password));

        using var store = CreateStore(
            new Dictionary<string, string?>
            {
                [ConfigKey] = material,
                [ConfigKey + ConfigurationChannelCertificateStore.PasswordSuffix] = TestCertificates.Password,
            },
            time);

        var first = store.Load(ConfigKey);

        await Assert.That(first.Certificate).IsNotNull();

        time.Advance(TimeSpan.FromDays(2));

        var second = store.Load(ConfigKey);

        await Assert.That(second.Certificate).IsNull();
        await Assert.That(second.Error).IsNotNull();
        await Assert.That(second.Error!.Contains("истёк", StringComparison.OrdinalIgnoreCase)).IsTrue();
    }

    [Test]
    public async Task Missing_key_or_value_is_reported_without_material()
    {
        using var store = CreateStore(new Dictionary<string, string?>());

        var noKey = store.Load(null);
        var emptyKey = store.Load("   ");
        var noValue = store.Load(ConfigKey);

        await Assert.That(noKey.Certificate).IsNull();
        await Assert.That(noKey.Error).IsNotNull();
        await Assert.That(emptyKey.Certificate).IsNull();
        await Assert.That(emptyKey.Error).IsNotNull();
        await Assert.That(noValue.Certificate).IsNull();
        await Assert.That(noValue.Error).IsNotNull();
    }


    [Test]
    public async Task Wrong_password_is_rejected_without_leaking_values()
    {
        using var certificate = TestCertificates.CreateSelfSigned("CN=lti-test-wrong-password");
        var material = Convert.ToBase64String(
            TestCertificates.ExportPfx(certificate, TestCertificates.Password));

        using var store = CreateStore(new Dictionary<string, string?>
        {
            [ConfigKey] = material,
            [ConfigKey + ConfigurationChannelCertificateStore.PasswordSuffix] = WrongPassword,
        });

        var result = store.Load(ConfigKey);

        await Assert.That(result.Certificate).IsNull();
        await Assert.That(result.Error).IsNotNull();
        await Assert.That(result.Error!.Contains(TestCertificates.Password, StringComparison.Ordinal)).IsFalse();
        await Assert.That(result.Error.Contains(WrongPassword, StringComparison.Ordinal)).IsFalse();
        await Assert.That(result.Error.Contains(material[..32], StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task Certificate_without_private_key_is_rejected()
    {
        using var certificate = TestCertificates.CreateSelfSigned("CN=lti-test-public-only");
        var material = Convert.ToBase64String(TestCertificates.ExportPfxWithoutPrivateKey(certificate));

        using var store = CreateStore(new Dictionary<string, string?>
        {
            [ConfigKey] = material,
        });

        var result = store.Load(ConfigKey);

        await Assert.That(result.Certificate).IsNull();
        await Assert.That(result.Error).IsNotNull();
        await Assert.That(result.Error!.Contains("приватн", StringComparison.OrdinalIgnoreCase)).IsTrue();
    }

    [Test]
    public async Task Expired_certificate_is_rejected()
    {
        using var certificate = TestCertificates.CreateSelfSigned(
            "CN=lti-test-expired",
            notBefore: DateTimeOffset.UtcNow.AddDays(-10),
            notAfter: DateTimeOffset.UtcNow.AddDays(-1));

        using var store = CreateStore(new Dictionary<string, string?>
        {
            [ConfigKey] = Convert.ToBase64String(
                TestCertificates.ExportPfx(certificate, TestCertificates.Password)),
            [ConfigKey + ConfigurationChannelCertificateStore.PasswordSuffix] = TestCertificates.Password,
        });

        var result = store.Load(ConfigKey);

        await Assert.That(result.Certificate).IsNull();
        await Assert.That(result.Error).IsNotNull();
        await Assert.That(result.Error!.Contains("истёк", StringComparison.OrdinalIgnoreCase)).IsTrue();
    }

    [Test]
    public async Task Not_yet_valid_certificate_is_rejected()
    {
        using var certificate = TestCertificates.CreateSelfSigned(
            "CN=lti-test-future",
            notBefore: DateTimeOffset.UtcNow.AddDays(1),
            notAfter: DateTimeOffset.UtcNow.AddDays(10));

        using var store = CreateStore(new Dictionary<string, string?>
        {
            [ConfigKey] = Convert.ToBase64String(
                TestCertificates.ExportPfx(certificate, TestCertificates.Password)),
            [ConfigKey + ConfigurationChannelCertificateStore.PasswordSuffix] = TestCertificates.Password,
        });

        var result = store.Load(ConfigKey);

        await Assert.That(result.Certificate).IsNull();
        await Assert.That(result.Error).IsNotNull();
        await Assert.That(result.Error!.Contains("ещё не наступил", StringComparison.OrdinalIgnoreCase)).IsTrue();
    }

    [Test]
    public async Task Diagnostics_return_expiry_for_expired_certificate()
    {
        var notAfter = DateTimeOffset.UtcNow.AddDays(-1);
        using var certificate = TestCertificates.CreateSelfSigned(
            "CN=lti-test-expired-diagnostics",
            notBefore: DateTimeOffset.UtcNow.AddDays(-10),
            notAfter: notAfter);

        var material = Convert.ToBase64String(
            TestCertificates.ExportPfx(certificate, TestCertificates.Password));

        using var store = CreateStore(new Dictionary<string, string?>
        {
            [ConfigKey] = material,
            [ConfigKey + ConfigurationChannelCertificateStore.PasswordSuffix] = TestCertificates.Password,
        });

        await Assert.That(store.Load(ConfigKey).Certificate).IsNull();

        var info = store.GetInfo(ConfigKey);

        await Assert.That(info.Found).IsTrue();
        await Assert.That(info.Valid).IsFalse();
        await Assert.That(info.ExpiresAt).IsNotNull();
        await Assert.That(info.Problem!.Contains("истёк", StringComparison.OrdinalIgnoreCase)).IsTrue();
        await Assert.That(info.Problem.Contains(material, StringComparison.Ordinal)).IsFalse();
        await Assert.That(info.Problem.Contains(TestCertificates.Password, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task Diagnostics_report_not_yet_valid_certificate()
    {
        using var certificate = TestCertificates.CreateSelfSigned(
            "CN=lti-test-future-diagnostics",
            notBefore: DateTimeOffset.UtcNow.AddDays(1),
            notAfter: DateTimeOffset.UtcNow.AddDays(10));

        using var store = CreateStore(new Dictionary<string, string?>
        {
            [ConfigKey] = Convert.ToBase64String(
                TestCertificates.ExportPfx(certificate, TestCertificates.Password)),
            [ConfigKey + ConfigurationChannelCertificateStore.PasswordSuffix] = TestCertificates.Password,
        });

        var info = store.GetInfo(ConfigKey);

        await Assert.That(info.Found).IsTrue();
        await Assert.That(info.Valid).IsFalse();
        await Assert.That(info.ExpiresAt).IsNotNull();
        await Assert.That(info.Problem!.Contains("ещё не наступил", StringComparison.OrdinalIgnoreCase)).IsTrue();
    }

    [Test]
    public async Task Diagnostics_report_certificate_without_private_key()
    {
        using var certificate = TestCertificates.CreateSelfSigned("CN=lti-test-public-only-diagnostics");

        using var store = CreateStore(new Dictionary<string, string?>
        {
            [ConfigKey] = Convert.ToBase64String(TestCertificates.ExportPfxWithoutPrivateKey(certificate)),
        });

        var info = store.GetInfo(ConfigKey);

        await Assert.That(info.Found).IsTrue();
        await Assert.That(info.Valid).IsFalse();
        await Assert.That(info.ExpiresAt).IsNotNull();
        await Assert.That(info.Problem!.Contains("приватн", StringComparison.OrdinalIgnoreCase)).IsTrue();
    }

    [Test]
    public async Task Diagnostics_report_missing_material()
    {
        using var store = CreateStore(new Dictionary<string, string?>());

        var info = store.GetInfo(ConfigKey);

        await Assert.That(info.Found).IsFalse();
        await Assert.That(info.Valid).IsFalse();
        await Assert.That(info.ExpiresAt).IsNull();
        await Assert.That(info.Problem).IsNotNull();
    }
}
