using LtiIntegrationGateway.Contracts.Messages.V1;
using LtiIntegrationGateway.Features.Configuration;
using LtiIntegrationGateway.Features.Inbound;
using LtiIntegrationGateway.Infrastructure.Rebus;
using LtiIntegrationGateway.Messaging;
using LtiIntegrationGateway.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Rebus.Activation;
using Rebus.Bus;
using Rebus.Config;
using Rebus.Transport.InMem;

namespace LtiIntegrationGateway.Tests.Features.Inbound;

/// <summary>
/// Синхронный режим при нескольких репликах: адрес возврата — reply-очередь экземпляра, поэтому ответ
/// продукта получает экземпляр, принявший вызов, а не любая реплика интеграции.
/// </summary>
public class SyncReplyQueueTests
{
    private const string Integration = "bank-a";
    private const string ProductQueue = "dpv.inbox";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    [Test]
    public async Task Reply_is_delivered_to_the_instance_that_accepted_the_call()
    {
        var network = new InMemNetwork();
        var serializer = new ProtobufSerializer(new ProtobufMessageRegistry());

        var received = new CollectingInboundHandler
        {
            ReplyFactory = message => new InboundResponse
            {
                MessageId = message.MessageId,
                HttpStatusCode = message.MessageId == "msg-a" ? 201 : 202,
            },
        };

        var productActivator = new BuiltinHandlerActivator();
        productActivator.Register(() => received);

        using var productBus = Configure.With(productActivator)
            .Transport(transport => transport.UseInMemoryTransport(network, ProductQueue))
            .Serialization(serialization => serialization.Register(_ => serializer))
            .Start();

        received.ReplyBus = productBus;

        using var gatewayBus = Configure.With(new BuiltinHandlerActivator())
            .Transport(transport => transport.UseInMemoryTransport(
                network,
                QueueNames.OutboundQueue(Integration)))
            .Serialization(serialization => serialization.Register(_ => serializer))
            .Start();

        using var first = new FlowInstance(network, serializer, gatewayBus, "replica-a");
        using var second = new FlowInstance(network, serializer, gatewayBus, "replica-b");

        await Assert.That(QueueNames.ReplyQueue(Integration, "replica-a"))
            .IsNotEqualTo(QueueNames.ReplyQueue(Integration, "replica-b"));

        var firstResponse = await first.Publisher.SendRequestAsync(
            ProductQueue,
            CreateMessage("msg-a"),
            RequestTimeout);
        var secondResponse = await second.Publisher.SendRequestAsync(
            ProductQueue,
            CreateMessage("msg-b"),
            RequestTimeout);

        await Assert.That(firstResponse.HttpStatusCode).IsEqualTo(201);
        await Assert.That(secondResponse.HttpStatusCode).IsEqualTo(202);
        await Assert.That((await received.WaitForCountAsync(2, RequestTimeout)).Count).IsEqualTo(2);
    }

    private static InboundMessage CreateMessage(string messageId) => new()
    {
        MessageId = messageId,
        ChannelName = "callbacks",
        ReplyExpected = true,
    };

    private sealed class FlowInstance : IDisposable
    {
        private readonly PendingInboundRequests _pending = new();
        private readonly IBus _replyBus;

        public FlowInstance(
            InMemNetwork network,
            ProtobufSerializer serializer,
            IBus gatewayBus,
            string instanceId)
        {
            var activator = new BuiltinHandlerActivator();

            activator.Register(() => new InboundResponseHandler(
                _pending,
                NullLogger<InboundResponseHandler>.Instance));

            _replyBus = Configure.With(activator)
                .Transport(transport => transport.UseInMemoryTransport(
                    network,
                    QueueNames.ReplyQueue(Integration, instanceId)))
                .Serialization(serialization => serialization.Register(_ => serializer))
                .Start();

            Publisher = new RebusInboundMessagePublisher(
                gatewayBus,
                Microsoft.Extensions.Options.Options.Create(new FlowOptions
                {
                    Integration = Integration,
                    InstanceId = instanceId,
                }),
                _pending);
        }

        public RebusInboundMessagePublisher Publisher { get; }

        public void Dispose() => _replyBus.Dispose();
    }
}
