using LtiIntegrationGateway.Contracts.Messages.V1;
using LtiIntegrationGateway.Messaging;

namespace LtiIntegrationGateway.Tests.Infrastructure.Rebus;

public class ProtobufMessageRegistryTests
{
    [Test]
    public async Task Registry_contains_every_message_of_contract_v1()
    {
        var registry = new ProtobufMessageRegistry();
        var missing = new List<string>();

        foreach (var descriptor in IntegrationMessagesReflection.Descriptor.MessageTypes)
        {
            var found = registry.Find(descriptor.FullName);

            if (found is null || found.FullName != descriptor.FullName)
            {
                missing.Add(descriptor.FullName);
            }
        }

        await Assert.That(string.Join(", ", missing)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Unknown_name_is_not_found()
    {
        var registry = new ProtobufMessageRegistry();

        await Assert.That(registry.Find("lti.gateway.messages.v1.Missing")).IsNull();
        await Assert.That(registry.Find(string.Empty)).IsNull();
    }
}
