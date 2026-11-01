namespace LtiIntegrationGateway.Infrastructure.Http;

/// <summary>
/// Аренда HTTP-обработчика, взятого из пула на время одного исходящего вызова. Освобождение
/// аренды возвращает обработчик пулу и позволяет освободить его после истечения срока жизни.
/// </summary>
public sealed class ChannelHttpClientLease : IDisposable
{
    private readonly Action _release;
    private int _released;

    public ChannelHttpClientLease(HttpMessageHandler handler, Action release)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(release);

        Handler = handler;
        _release = release;
    }

    /// <summary>Обработчик соединений, поверх которого на время вызова создаётся <see cref="HttpClient"/>.</summary>
    public HttpMessageHandler Handler { get; }

    /// <summary>Освобождает аренду; повторный вызов ничего не делает.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0)
        {
            _release();
        }
    }
}
