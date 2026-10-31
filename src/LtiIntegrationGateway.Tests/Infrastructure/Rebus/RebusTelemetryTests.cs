using LtiIntegrationGateway.Tests.TestSupport;
using Rebus.Activation;
using Rebus.Config;
using Rebus.Diagnostics;
using Rebus.Handlers;
using Rebus.Retry.Simple;
using Rebus.Transport.InMem;

namespace LtiIntegrationGateway.Tests.Infrastructure.Rebus;

/// <summary>Проверка того, что Rebus публикует диагностические activity при обработке сообщения.</summary>
public class RebusTelemetryTests
{
    [Test]
    public async Task Message_processing_emits_rebus_activity()
    {
        using var collector = new TelemetryCollector(RebusDiagnosticConstants.ActivitySourceName);

        var handled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var activator = new BuiltinHandlerActivator();
        activator.Register(() => new TestHandler(handled));

        using var bus = Configure.With(activator)
            .Transport(transport => transport.UseInMemoryTransport(new InMemNetwork(), "telemetry-test"))
            .Options(options => options.EnableDiagnosticSources())
            .Start();

        await bus.SendLocal(new TestMessage("marker"));

        await handled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);

        while (DateTimeOffset.UtcNow < deadline && collector.Activities.Count == 0)
        {
            await Task.Delay(20);
        }

        await Assert.That(collector.Activities.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task Rebus_activity_does_not_expose_exception_text()
    {
        using var collector = new TelemetryCollector(RebusDiagnosticConstants.ActivitySourceName);

        var activator = new BuiltinHandlerActivator();
        activator.Register(() => new FailingHandler());

        using var bus = Configure.With(activator)
            .Transport(transport => transport.UseInMemoryTransport(new InMemNetwork(), "telemetry-failure"))
            .Options(options =>
            {
                options.EnableDiagnosticSources();
                options.RetryStrategy(maxDeliveryAttempts: 1, errorQueueName: "telemetry-error");
            })
            .Start();

        await bus.SendLocal(new TestMessage("marker"));

        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);

        while (DateTimeOffset.UtcNow < deadline && collector.Activities.Count == 0)
        {
            await Task.Delay(20);
        }

        foreach (var activity in collector.Activities)
        {
            var values = activity.TagObjects
                .Select(tag => tag.Value?.ToString() ?? string.Empty)
                .Concat(activity.Events.SelectMany(@event => @event.Tags)
                    .Select(tag => tag.Value?.ToString() ?? string.Empty));

            await Assert.That(values.Any(value => value.Contains("MARKER-REBUS", StringComparison.Ordinal))).IsFalse();
        }
    }

    private sealed record TestMessage(string Value);

    private sealed class TestHandler(TaskCompletionSource handled) : IHandleMessages<TestMessage>
    {
        public Task Handle(TestMessage message)
        {
            handled.TrySetResult();

            return Task.CompletedTask;
        }
    }

    private sealed class FailingHandler : IHandleMessages<TestMessage>
    {
        public Task Handle(TestMessage message) =>
            throw new InvalidOperationException("MARKER-REBUS");
    }
}
