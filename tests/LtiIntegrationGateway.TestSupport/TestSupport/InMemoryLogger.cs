using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace LtiIntegrationGateway.Tests.TestSupport;

/// <summary>Логгер, складывающий записи в общую очередь провайдера.</summary>
internal sealed class InMemoryLogger(string category, ConcurrentQueue<CapturedLogEntry> entries) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        entries.Enqueue(new CapturedLogEntry(category, logLevel, formatter(state, exception)));
    }
}
