using System.Collections.Concurrent;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace LtiIntegrationGateway.Infrastructure.Http;

/// <summary>
/// Пул HTTP-обработчиков исходящих каналов: соединения изолированы по ключу
/// «базовый адрес, тип авторизации, отпечаток сертификата» (ADR §7.7).
/// </summary>
/// <remarks>
/// Обработчик создаётся один раз на ключ и переиспользуется всеми вызовами канала. Запись пула
/// имеет срок жизни: по его истечении создаётся новый обработчик с новыми соединениями, а прежний
/// освобождается после завершения уже начатых вызовов. Истёкшие записи удаляются при обращении к
/// пулу не чаще <see cref="CleanupInterval"/>, поэтому обработчики сменённых ревизий не удерживают
/// соединения бесконечно; вытеснения по количеству ключей нет — это открытый вопрос (README).
/// </remarks>
public sealed class ChannelHttpClientPool : IChannelHttpClientFactory, IDisposable
{
    /// <summary>Срок жизни записи пула; после него обработчик создаётся заново.</summary>
    public static readonly TimeSpan EntryLifetime = TimeSpan.FromMinutes(15);

    /// <summary>Время жизни соединения внутри обработчика (ADR §7.7).</summary>
    public static readonly TimeSpan ConnectionLifetime = TimeSpan.FromMinutes(5);

    /// <summary>Минимальный интервал уборки истёкших записей: обход словаря не выполняется на каждом вызове.</summary>
    public static readonly TimeSpan CleanupInterval = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<ChannelHttpClientKey, Entry> _entries = new();
    private readonly TimeProvider _timeProvider;
    private long _nextCleanupTicks = long.MinValue;
    private int _disposed;

    public ChannelHttpClientPool(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        _timeProvider = timeProvider;
    }

    public ChannelHttpClientLease Acquire(ChannelHttpClientKey key, X509Certificate2? clientCertificate)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (key.CertificateThumbprint is not null && clientCertificate is null)
        {
            throw new InvalidOperationException(
                "ключ канала содержит отпечаток сертификата, но клиентский сертификат не передан");
        }

        ThrowIfDisposed();

        CleanupExpiredEntries(_timeProvider.GetUtcNow());

        while (true)
        {
            ThrowIfDisposed();

            var now = _timeProvider.GetUtcNow();

            if (!_entries.TryGetValue(key, out var entry))
            {
                var created = new Entry(CreateHandler(clientCertificate), now);

                if (_entries.TryAdd(key, created))
                {
                    if (IsDisposed)
                    {
                        // Пул остановлен между проверкой и добавлением: аренда не выдаётся.
                        _entries.TryRemove(KeyValuePair.Create(key, created));
                        created.Retire();

                        ThrowIfDisposed();
                    }

                    if (created.TryAcquire())
                    {
                        return new ChannelHttpClientLease(created.Handler, created.Release);
                    }

                    _entries.TryRemove(KeyValuePair.Create(key, created));
                    created.Retire();

                    continue;
                }

                created.Retire();

                continue;
            }

            if (entry.IsExpired(now))
            {
                var replacement = new Entry(CreateHandler(clientCertificate), now);

                if (_entries.TryUpdate(key, replacement, entry))
                {
                    entry.Retire();

                    if (IsDisposed)
                    {
                        _entries.TryRemove(KeyValuePair.Create(key, replacement));
                        replacement.Retire();

                        ThrowIfDisposed();
                    }

                    continue;
                }

                // Запись успел заменить другой поток: новый обработчик не нужен.
                replacement.Retire();

                continue;
            }

            if (entry.TryAcquire())
            {
                return new ChannelHttpClientLease(entry.Handler, entry.Release);
            }

            // Обработчик уже выведен из пула другим потоком: повторяем с актуальной записью.
            _entries.TryRemove(KeyValuePair.Create(key, entry));
        }
    }

    private bool IsDisposed => Volatile.Read(ref _disposed) == 1;

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(IsDisposed, this);

    /// <summary>
    /// Удаляет записи с истёкшим сроком жизни, чтобы после смены ревизии или сертификата старые
    /// обработчики не удерживали соединения бесконечно (ADR §7.7). Обход словаря выполняется не
    /// чаще <see cref="CleanupInterval"/>: истёкшая запись всё равно пересоздаётся при обращении
    /// к её ключу, поэтому уборка на каждом вызове не нужна.
    /// </summary>
    private void CleanupExpiredEntries(DateTimeOffset now)
    {
        if (now.UtcTicks < Volatile.Read(ref _nextCleanupTicks))
        {
            return;
        }

        var scheduled = now.Add(CleanupInterval).UtcTicks;

        if (Interlocked.Exchange(ref _nextCleanupTicks, scheduled) > now.UtcTicks)
        {
            // Уборку уже запланировал другой поток.
            return;
        }

        foreach (var pair in _entries)
        {
            if (pair.Value.IsExpired(now) && _entries.TryRemove(pair))
            {
                pair.Value.Retire();
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        foreach (var pair in _entries)
        {
            if (_entries.TryRemove(pair))
            {
                pair.Value.Retire();
            }
        }
    }

    /// <summary>
    /// Создаёт обработчик с обязательными ограничениями: без автоматических редиректов, TLS не
    /// ниже 1.2 и без отключения проверки серверного сертификата (ADR §8.7, §13).
    /// </summary>
    private static SocketsHttpHandler CreateHandler(X509Certificate2? clientCertificate)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            PooledConnectionLifetime = ConnectionLifetime,
            SslOptions = new SslClientAuthenticationOptions
            {
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            },
        };

        if (clientCertificate is not null)
        {
            handler.SslOptions.ClientCertificates = new X509CertificateCollection { clientCertificate };
        }

        return handler;
    }

    /// <summary>Запись пула: обработчик, время создания и счётчик активных использований.</summary>
    private sealed class Entry(SocketsHttpHandler handler, DateTimeOffset createdAt)
    {
        private readonly Lock _gate = new();
        private int _references;
        private bool _retired;
        private bool _disposed;

        public SocketsHttpHandler Handler { get; } = handler;

        public bool IsExpired(DateTimeOffset now) => now - createdAt >= EntryLifetime;

        /// <summary>Захватывает обработчик; отказ, если запись уже выведена из пула.</summary>
        public bool TryAcquire()
        {
            lock (_gate)
            {
                if (_retired)
                {
                    return false;
                }

                _references++;

                return true;
            }
        }

        /// <summary>Освобождает использование; последний освободивший выведенную запись её и закрывает.</summary>
        public void Release()
        {
            lock (_gate)
            {
                if (_references > 0)
                {
                    _references--;
                }

                if (_references == 0 && _retired)
                {
                    DisposeCore();
                }
            }
        }

        /// <summary>Выводит запись из пула; обработчик закрывается после завершения текущих вызовов.</summary>
        public void Retire()
        {
            lock (_gate)
            {
                _retired = true;

                if (_references == 0)
                {
                    DisposeCore();
                }
            }
        }

        private void DisposeCore()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            Handler.Dispose();
        }
    }
}

