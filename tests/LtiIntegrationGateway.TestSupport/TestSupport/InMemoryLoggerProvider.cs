using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace LtiIntegrationGateway.Tests.TestSupport;

/// <summary>Запись лога, перехваченная в тесте.</summary>
public sealed record CapturedLogEntry(string Category, LogLevel Level, string Message);

/// <summary>Провайдер логов, складывающий записи в память для проверок в тестах.</summary>
public sealed class InMemoryLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<CapturedLogEntry> _entries = new();

    public IReadOnlyCollection<CapturedLogEntry> Entries => _entries.ToArray();

    public ILogger CreateLogger(string categoryName) => new InMemoryLogger(categoryName, _entries);

    public void Dispose()
    {
    }
}

