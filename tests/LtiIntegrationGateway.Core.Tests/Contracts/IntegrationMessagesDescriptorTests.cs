using Google.Protobuf.Reflection;
using LtiIntegrationGateway.Contracts.Messages.V1;

namespace LtiIntegrationGateway.Tests.Contracts;

public class IntegrationMessagesDescriptorTests
{
    private const string AnyTypeName = "google.protobuf.Any";

    [Test]
    public async Task No_message_contains_google_protobuf_any()
    {
        var fieldsWithAny = new List<string>();

        foreach (var messageType in IntegrationMessagesReflection.Descriptor.MessageTypes)
        {
            CollectAnyFields(messageType, fieldsWithAny);
        }

        await Assert.That(string.Join(", ", fieldsWithAny)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Message_full_names_are_stable()
    {
        await Assert
            .That(OutboundRequest.Descriptor.FullName)
            .IsEqualTo("lti.gateway.messages.v1.OutboundRequest");
        await Assert
            .That(OutboundResult.Descriptor.FullName)
            .IsEqualTo("lti.gateway.messages.v1.OutboundResult");
        await Assert
            .That(InboundMessage.Descriptor.FullName)
            .IsEqualTo("lti.gateway.messages.v1.InboundMessage");
    }

    private static void CollectAnyFields(MessageDescriptor message, List<string> fieldsWithAny)
    {
        foreach (var field in message.Fields.InDeclarationOrder())
        {
            if (field.FieldType == FieldType.Message && field.MessageType.FullName == AnyTypeName)
            {
                fieldsWithAny.Add($"{message.FullName}.{field.Name}");
            }
        }

        foreach (var nested in message.NestedTypes)
        {
            CollectAnyFields(nested, fieldsWithAny);
        }
    }
}
