using System.Security.Cryptography.X509Certificates;
using LtiIntegrationGateway.Infrastructure.Http;

namespace LtiIntegrationGateway.Tests.TestSupport;

/// <summary>Ключ канала и сертификат, с которыми был запрошен обработчик.</summary>
public sealed record AcquiredChannelClient(ChannelHttpClientKey Key, X509Certificate2? Certificate);

/// <summary>
/// Подмена пула обработчиков: вызовы получают тестовый транспорт, а тест видит, с каким ключом
/// канала и каким сертификатом выполнялся каждый вызов.
/// </summary>
public sealed class RecordingChannelHttpClientFactory(RecordingHttpMessageHandler handler)
    : IChannelHttpClientFactory
{
    private readonly List<AcquiredChannelClient> _acquired = [];

    public IReadOnlyList<AcquiredChannelClient> Acquired
    {
        get
        {
            lock (_acquired)
            {
                return _acquired.ToArray();
            }
        }
    }

    public ChannelHttpClientLease Acquire(ChannelHttpClientKey key, X509Certificate2? clientCertificate)
    {
        lock (_acquired)
        {
            _acquired.Add(new AcquiredChannelClient(key, clientCertificate));
        }

        return new ChannelHttpClientLease(handler, static () => { });
    }
}
