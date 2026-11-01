using System.Text.RegularExpressions;

namespace LtiIntegrationGateway.Infrastructure.Rebus;

/// <summary>
/// Имена очередей шины: входная очередь интеграции, error-queue интеграции и очередь продукта,
/// которая вычисляется из <c>product_id</c> и не задаётся произвольным значением.
/// </summary>
/// <remarks>
/// Адрес содержит интеграцию, а не экземпляр сервиса: количество реплик меняет только число
/// конкурирующих потребителей одной очереди. Имя интеграции обязано соответствовать шаблону
/// <c>^[a-z][a-z0-9-]{1,31}$</c>; адрес с другим именем отклоняется.
/// </remarks>
public static partial class QueueNames
{
    /// <summary>Таблица сообщений транспорта PostgreSQL: общая для всех участников шины.</summary>
    public const string MessageTable = "messages";

    /// <summary>Максимальная длина имени интеграции.</summary>
    public const int MaxIntegrationLength = 32;

    /// <summary>Максимальная длина идентификатора продукта.</summary>
    public const int MaxProductIdLength = 32;

    /// <summary>Входная очередь интеграции: сюда продукты отправляют <c>OutboundRequest</c>.</summary>
    public static string OutboundQueue(string integrationId) =>
        $"lti.gateway.{RequiredName(integrationId)}.outbound";

    /// <summary>
    /// Error-queue интеграции: сюда попадают сообщения этой интеграции, не обработанные после всех
    /// попыток.
    /// </summary>
    public static string ErrorQueue(string integrationId) =>
        $"lti.gateway.{RequiredName(integrationId)}.error";

    /// <summary>
    /// Reply-очередь экземпляра для синхронного входящего режима: ответ продукта возвращается именно
    /// тому экземпляру, который принял вызов, даже если у интеграции несколько реплик.
    /// </summary>
    public static string ReplyQueue(string integrationId, string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);

        return $"lti.gateway.{RequiredName(integrationId)}.reply.{instanceId}";
    }

    /// <summary>
    /// Адрес очереди продукта в формате <c>&lt;product_id&gt;.inbox</c>: в неё доставляются
    /// <c>OutboundResult</c> и <c>InboundMessage</c> продукта.
    /// </summary>
    public static string ProductInbox(string productId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);

        return $"{productId}.inbox";
    }

    /// <summary>
    /// Проверяет, что адрес очереди имеет формат <c>&lt;product_id&gt;.inbox</c>, где <c>product_id</c>
    /// соответствует шаблону <c>^[a-z][a-z0-9-]{1,31}$</c>. Пустое значение и <c>null</c> не проходят.
    /// </summary>
    public static bool IsProductInbox(string? queueAddress) =>
        queueAddress is not null && InboxAddressPattern().IsMatch(queueAddress);

    /// <summary>
    /// Проверяет, что имя интеграции соответствует шаблону <c>^[a-z][a-z0-9-]{1,31}$</c>.
    /// Пустое значение и <c>null</c> не проходят.
    /// </summary>
    public static bool IsIntegrationId(string? integrationId) =>
        integrationId is not null && IntegrationPattern().IsMatch(integrationId);

    /// <summary>
    /// Проверяет, что адрес является входной очередью интеграции, и возвращает её имя.
    /// </summary>
    public static bool TryParseOutboundQueue(string? queueAddress, out string? integrationId)
    {
        integrationId = null;

        if (queueAddress is null)
        {
            return false;
        }

        var match = OutboundQueuePattern().Match(queueAddress);

        if (!match.Success)
        {
            return false;
        }

        integrationId = match.Groups["integration"].Value;

        return true;
    }

    /// <summary>Возвращает имя интеграции либо отклоняет значение, не соответствующее шаблону.</summary>
    private static string RequiredName(string integrationId)
    {
        if (!IsIntegrationId(integrationId))
        {
            throw new ArgumentException(
                $"Имя интеграции '{integrationId ?? "<null>"}' не соответствует шаблону ^[a-z][a-z0-9-]{{1,31}}$.",
                nameof(integrationId));
        }

        return integrationId;
    }

    [GeneratedRegex("^[a-z][a-z0-9-]{1,31}\\.inbox$", RegexOptions.CultureInvariant)]
    private static partial Regex InboxAddressPattern();

    [GeneratedRegex("^[a-z][a-z0-9-]{1,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex IntegrationPattern();

    [GeneratedRegex(
        "^lti\\.gateway\\.(?<integration>[a-z][a-z0-9-]{1,31})\\.outbound$",
        RegexOptions.CultureInvariant)]
    private static partial Regex OutboundQueuePattern();
}
