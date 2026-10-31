using Rebus.Bus;
using Rebus.Bus.Advanced;
using Rebus.DataBus;
using Rebus.Routing;

namespace LtiIntegrationGateway.Tests.TestSupport;

/// <summary>Сообщение, зафиксированное тестовой шиной.</summary>
public sealed record SentMessage(string Address, object Message);

/// <summary>
/// Шина для тестов диспетчера: фиксирует адрес и сообщение каждой отправки, а по условию теста
/// имитирует сбой <c>Send</c>. Остальные операции <see cref="IBus"/> в сценариях не участвуют.
/// </summary>
public sealed class RecordingBus(Func<string, object, bool>? shouldFail = null) : IBus
{
    private readonly List<SentMessage> _sent = [];
    private readonly Func<string, object, bool>? _shouldFail = shouldFail;

    private IAdvancedApi? _advanced;

    /// <summary>Отправки в порядке выполнения.</summary>
    public IReadOnlyList<SentMessage> Sent
    {
        get
        {
            lock (_sent)
            {
                return _sent.ToArray();
            }
        }
    }

    public IAdvancedApi Advanced => _advanced ??= new RecordingAdvancedApi(this);

    public Task Send(object commandMessage, IDictionary<string, string>? optionalHeaders = null) =>
        NotSupported(nameof(Send));

    public Task SendLocal(object commandMessage, IDictionary<string, string>? optionalHeaders = null) =>
        NotSupported(nameof(SendLocal));

    public Task Defer(TimeSpan delay, object message, IDictionary<string, string>? optionalHeaders = null) =>
        NotSupported(nameof(Defer));

    public Task DeferLocal(TimeSpan delay, object message, IDictionary<string, string>? optionalHeaders = null) =>
        NotSupported(nameof(DeferLocal));

    public Task Reply(object replyMessage, IDictionary<string, string>? optionalHeaders = null) =>
        NotSupported(nameof(Reply));

    public Task Subscribe<TEvent>() => NotSupported(nameof(Subscribe));

    public Task Subscribe(Type eventType) => NotSupported(nameof(Subscribe));

    public Task Unsubscribe<TEvent>() => NotSupported(nameof(Unsubscribe));

    public Task Unsubscribe(Type eventType) => NotSupported(nameof(Unsubscribe));

    public Task Publish(object eventMessage, IDictionary<string, string>? optionalHeaders = null) =>
        NotSupported(nameof(Publish));

    public void Dispose()
    {
    }

    private static Task NotSupported(string operation) =>
        throw new NotSupportedException($"тестовая шина не поддерживает '{operation}'");

    /// <summary>Фиксирует отправку; при заданном условии имитирует сбой транспорта.</summary>
    private void Record(string address, object message)
    {
        if (_shouldFail?.Invoke(address, message) == true)
        {
            throw new InvalidOperationException("тестовая шина: отправка не удалась");
        }

        lock (_sent)
        {
            _sent.Add(new SentMessage(address, message));
        }
    }

    private sealed class RecordingAdvancedApi(RecordingBus bus) : IAdvancedApi
    {
        private readonly RecordingRoutingApi _routing = new(bus);

        public IWorkersApi Workers => throw new NotSupportedException("тестовая шина не поддерживает 'Workers'");

        public ITopicsApi Topics => throw new NotSupportedException("тестовая шина не поддерживает 'Topics'");

        public IRoutingApi Routing => _routing;

        public ITransportMessageApi TransportMessage =>
            throw new NotSupportedException("тестовая шина не поддерживает 'TransportMessage'");

        public IDataBus DataBus => throw new NotSupportedException("тестовая шина не поддерживает 'DataBus'");

        public ISyncBus SyncBus => throw new NotSupportedException("тестовая шина не поддерживает 'SyncBus'");
    }

    private sealed class RecordingRoutingApi(RecordingBus bus) : IRoutingApi
    {
        public Task Send(string destinationAddress, object message, IDictionary<string, string>? optionalHeaders = null)
        {
            bus.Record(destinationAddress, message);

            return Task.CompletedTask;
        }

        public Task SendRoutingSlip(
            Itinerary itinerary,
            object message,
            IDictionary<string, string>? optionalHeaders = null) =>
            throw new NotSupportedException("тестовая шина не поддерживает 'SendRoutingSlip'");

        public Task Defer(
            string destinationAddress,
            TimeSpan delay,
            object message,
            IDictionary<string, string>? optionalHeaders = null) =>
            throw new NotSupportedException("тестовая шина не поддерживает 'Defer'");
    }
}
