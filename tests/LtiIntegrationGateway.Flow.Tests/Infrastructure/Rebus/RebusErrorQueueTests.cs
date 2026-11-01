using LtiIntegrationGateway.Contracts.Messages.V1;
using LtiIntegrationGateway.Features.Outbound;
using LtiIntegrationGateway.Infrastructure.Rebus;
using LtiIntegrationGateway.Messaging;
using Rebus.Activation;
using Rebus.Config;
using Rebus.Handlers;
using Rebus.Transport.InMem;

namespace LtiIntegrationGateway.Tests.Infrastructure.Rebus;

/// <summary>
/// Error-queue шины: сообщение, не обработанное после всех попыток, оказывается в очереди
/// <c>lti.gateway.&lt;integration&gt;.error</c>, а не в производной от входной очереди.
/// </summary>
public class RebusErrorQueueTests
{
    private const string Integration = "bank-a";
    private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(15);
    private static readonly string OutboundQueue = QueueNames.OutboundQueue(Integration);
    private static readonly string ErrorQueue = QueueNames.ErrorQueue(Integration);

    [Test]
    public async Task Message_that_fails_all_attempts_lands_in_gateway_error_queue()
    {
        var network = new InMemNetwork();
        var activator = new BuiltinHandlerActivator();

        activator.Register(() => new FailingHandler());

        var serializer = new ProtobufSerializer(new ProtobufMessageRegistry());

        // Конфигурация повторяет приложение: входная очередь интеграции и её error-queue;
        // значения числа попыток и кулдауна — по умолчанию Rebus.
        using var bus = Configure.With(activator)
            .Transport(transport => transport.UseInMemoryTransport(network, OutboundQueue))
            .Options(options => RebusConfigurationExtensions.ConfigureRetryStrategy(options, Integration))
            .Serialization(serialization => serialization.Register(_ => serializer))
            .Start();

        await bus.SendLocal(new OutboundRequest
        {
            MessageId = "error-1",
            ProductId = "dpv",
            ChannelName = "bank-a",
            RelativePath = "/api/contracts",
            HttpMethod = "GET",
        });

        var count = await WaitForQueueAsync(network, ErrorQueue, DeliveryTimeout);

        await Assert.That(count).IsEqualTo(1);
        await Assert.That(network.HasQueue($"{OutboundQueue}.error")).IsFalse();
    }

    [Test]
    public async Task Invalid_outbound_request_lands_in_error_queue_without_retries()
    {
        var network = new InMemNetwork();
        var activator = new BuiltinHandlerActivator();

        activator.Register(() => new OutboundRequestHandler(null!));

        var serializer = new ProtobufSerializer(new ProtobufMessageRegistry());

        using var bus = Configure.With(activator)
            .Transport(transport => transport.UseInMemoryTransport(network, OutboundQueue))
            .Options(options => RebusConfigurationExtensions.ConfigureRetryStrategy(options, Integration))
            .Serialization(serialization => serialization.Register(_ => serializer))
            .Start();

        await bus.SendLocal(new OutboundRequest());

        var count = await WaitForQueueAsync(network, ErrorQueue, DeliveryTimeout);

        await Assert.That(count).IsEqualTo(1);
        await Assert.That(network.HasQueue($"{OutboundQueue}.error")).IsFalse();
    }

    /// <summary>Ждёт появления сообщения в очереди in-memory сети и возвращает число сообщений.</summary>
    private static async Task<int> WaitForQueueAsync(InMemNetwork network, string queue, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (network.HasQueue(queue) && network.GetCount(queue) > 0)
            {
                return network.GetCount(queue);
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"сообщение не попало в очередь '{queue}' за {timeout}");
    }

    private sealed class FailingHandler : IHandleMessages<OutboundRequest>
    {
        public Task Handle(OutboundRequest message) =>
            throw new InvalidOperationException("тестовый обработчик всегда завершается ошибкой");
    }
}
