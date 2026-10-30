using Google.Protobuf;

namespace LtiIntegrationGateway.Tests.TestSupport;

/// <summary>
/// Профиль кодирования контрактов: ProtoJSON с игнорированием неизвестных полей.
/// </summary>
/// <remarks>
/// Повторяет нормативный профиль из шапки `integration_messages.proto`. Сериализатор Rebus
/// появится отдельным change и должен вести себя так же — тесты контракта фиксируют эталон.
/// </remarks>
public static class ContractJson
{
    private static readonly JsonParser Parser = new(JsonParser.Settings.Default.WithIgnoreUnknownFields(true));

    public static string Serialize(IMessage message) => JsonFormatter.Default.Format(message);

    public static T Parse<T>(string json)
        where T : IMessage<T>, new() => Parser.Parse<T>(json);
}
