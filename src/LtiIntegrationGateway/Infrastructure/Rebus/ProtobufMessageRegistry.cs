using Google.Protobuf.Reflection;
using LtiIntegrationGateway.Contracts.Messages.V1;

namespace LtiIntegrationGateway.Infrastructure.Rebus;

/// <summary>
/// Реестр интеграционных сообщений: полное имя сообщения → дескриптор.
/// </summary>
/// <remarks>
/// Список дескрипторов задан явно, а не собран рефлексией по сборке: рефлексия захватила бы и
/// служебные типы, а полноту списка проверяет тест, сверяющий его со всеми сообщениями окружения
/// <c>lti.gateway.messages.v1</c>.
/// </remarks>
public sealed class ProtobufMessageRegistry
{
    private readonly Dictionary<string, MessageDescriptor> _descriptors;

    public ProtobufMessageRegistry()
        : this(
        [
            OutboundRequest.Descriptor,
            OutboundResult.Descriptor,
            InboundMessage.Descriptor,
            InboundResponse.Descriptor,
            InboundResponseHeader.Descriptor,
            QueryParameter.Descriptor,
        ])
    {
    }

    public ProtobufMessageRegistry(IEnumerable<MessageDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);

        var all = descriptors.ToArray();

        _descriptors = all.ToDictionary(descriptor => descriptor.FullName, StringComparer.Ordinal);
        Types = TypeRegistry.FromMessages(all);
    }

    /// <summary>Реестр типов для разбора сообщений (нужен <c>JsonParser</c>).</summary>
    public TypeRegistry Types { get; }

    /// <summary>Возвращает дескриптор по полному имени сообщения или <c>null</c>, если тип не зарегистрирован.</summary>
    public MessageDescriptor? Find(string fullName) =>
        string.IsNullOrWhiteSpace(fullName) ? null : _descriptors.GetValueOrDefault(fullName);
}
