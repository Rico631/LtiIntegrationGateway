using System.Text;
using Google.Protobuf;
using Rebus.Messages;
using Rebus.Serialization;

namespace LtiIntegrationGateway.Messaging;

/// <summary>
/// Сериализатор сообщений Rebus на основе Protobuf: тело — ProtoJSON, тип — заголовок
/// <c>rbs2-msg-type</c>.
/// </summary>
/// <remarks>
/// Профиль кодирования повторяет нормативный профиль из шапки <c>integration_messages.proto</c>:
/// camelCase, <c>bytes</c> как base64, неизвестные поля игнорируются при разборе.
/// </remarks>
public sealed class ProtobufSerializer(ProtobufMessageRegistry registry) : ISerializer
{
    private const string ContentType = "application/json";

    private readonly JsonParser _parser = new(
        JsonParser.Settings.Default
            .WithIgnoreUnknownFields(true)
            .WithTypeRegistry(registry.Types));

    public Task<TransportMessage> Serialize(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.Body is not IMessage protobufMessage)
        {
            throw new InvalidOperationException(
                $"Сообщение типа '{message.Body.GetType().FullName}' не является Protobuf-сообщением " +
                "и не может быть отправлено через шину.");
        }

        var body = Encoding.UTF8.GetBytes(JsonFormatter.Default.Format(protobufMessage));

        var headers = new Dictionary<string, string>(message.Headers, StringComparer.Ordinal)
        {
            [Headers.Type] = protobufMessage.Descriptor.FullName,
            [Headers.ContentType] = ContentType,
        };

        return Task.FromResult(new TransportMessage(headers, body));
    }

    public Task<Message> Deserialize(TransportMessage transportMessage)
    {
        ArgumentNullException.ThrowIfNull(transportMessage);

        if (!transportMessage.Headers.TryGetValue(Headers.Type, out var fullName) ||
            string.IsNullOrWhiteSpace(fullName))
        {
            throw new InvalidOperationException(
                $"Транспортное сообщение не содержит заголовок типа '{Headers.Type}', " +
                "поэтому тип сообщения определить нельзя.");
        }

        var descriptor = registry.Find(fullName)
            ?? throw new InvalidOperationException(
                $"Тип сообщения '{fullName}' не зарегистрирован в контракте и не может быть разобран.");

        var body = Encoding.UTF8.GetString(transportMessage.Body);
        var protobufMessage = _parser.Parse(body, descriptor);

        var headers = new Dictionary<string, string>(transportMessage.Headers, StringComparer.Ordinal);

        return Task.FromResult(new Message(headers, protobufMessage));
    }
}
