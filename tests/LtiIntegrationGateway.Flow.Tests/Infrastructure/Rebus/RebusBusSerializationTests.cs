using LtiIntegrationGateway.Contracts.Messages.V1;
using LtiIntegrationGateway.Messaging;
using Rebus.Activation;
using Rebus.Config;
using Rebus.Handlers;
using Rebus.Transport.InMem;

namespace LtiIntegrationGateway.Tests.Infrastructure.Rebus;

public class RebusBusSerializationTests
{
    [Test]
    public async Task Message_passes_through_bus_with_same_type_and_values()
    {
        var network = new InMemNetwork();
        var activator = new BuiltinHandlerActivator();
        var handler = new CollectingHandler();
        var serializer = new ProtobufSerializer(new ProtobufMessageRegistry());

        activator.Register(() => handler);

        using var bus = Configure.With(activator)
            .Transport(transport => transport.UseInMemoryTransport(network, "test.input"))
            .Serialization(serialization => serialization.Register(_ => serializer))
            .Start();

        await bus.SendLocal(new OutboundRequest
        {
            MessageId = "bus-1",
            ProductId = "dpv",
            ChannelName = "bank-a",
            RelativePath = "/api/contracts",
            HttpMethod = "GET",
        });

        var received = await handler.Received.WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That(received.MessageId).IsEqualTo("bus-1");
        await Assert.That(received.ProductId).IsEqualTo("dpv");
        await Assert.That(received.RelativePath).IsEqualTo("/api/contracts");
        await Assert.That(received.HttpMethod).IsEqualTo("GET");
    }

    private sealed class CollectingHandler : IHandleMessages<OutboundRequest>
    {
        private readonly TaskCompletionSource<OutboundRequest> _received =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<OutboundRequest> Received => _received.Task;

        public Task Handle(OutboundRequest message)
        {
            _received.TrySetResult(message);

            return Task.CompletedTask;
        }
    }
}
