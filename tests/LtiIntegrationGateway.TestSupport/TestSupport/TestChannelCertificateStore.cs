using LtiIntegrationGateway.Infrastructure.Security;
using Microsoft.Extensions.Configuration;

namespace LtiIntegrationGateway.Tests.TestSupport;

/// <summary>
/// Материал клиентских сертификатов для тестов: значения задаются в самом тесте и загружаются
/// реальным <see cref="ConfigurationChannelCertificateStore"/> поверх in-memory конфигурации.
/// </summary>
public sealed class TestChannelCertificateStore : IChannelCertificateStore, IDisposable
{
    private readonly Dictionary<string, (string Material, string? Password)> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ConfigurationChannelCertificateStore> _stores = new(StringComparer.Ordinal);

    public TestChannelCertificateStore With(string configKey, string material, string? password = null)
    {
        _values[configKey] = (material, password);

        return this;
    }

    public ChannelCertificateResult Load(string? configKey)
    {
        if (string.IsNullOrWhiteSpace(configKey) || !_values.TryGetValue(configKey, out var value))
        {
            return ChannelCertificateResult.Failed("материал сертификата не настроен");
        }

        return GetStore(configKey, value).Load(configKey);
    }

    public ChannelCertificateInfo GetInfo(string? configKey)
    {
        if (string.IsNullOrWhiteSpace(configKey) || !_values.TryGetValue(configKey, out var value))
        {
            return ChannelCertificateInfo.Missing("материал сертификата не настроен");
        }

        return GetStore(configKey, value).GetInfo(configKey);
    }

    /// <summary>
    /// Один экземпляр реального хранилища на ключ: кэш сертификатов повторяет поведение приложения.
    /// </summary>
    private ConfigurationChannelCertificateStore GetStore(
        string configKey,
        (string Material, string? Password) value)
    {
        lock (_stores)
        {
            if (_stores.TryGetValue(configKey, out var existing))
            {
                return existing;
            }

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [configKey] = value.Material,
                    [configKey + ConfigurationChannelCertificateStore.PasswordSuffix] = value.Password,
                })
                .Build();

            var store = new ConfigurationChannelCertificateStore(configuration, TimeProvider.System);

            _stores[configKey] = store;

            return store;
        }
    }

    public void Dispose()
    {
        foreach (var store in _stores.Values)
        {
            store.Dispose();
        }

        _stores.Clear();
    }
}

