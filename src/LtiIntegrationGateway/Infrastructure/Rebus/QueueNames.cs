using System.Text.RegularExpressions;

namespace LtiIntegrationGateway.Infrastructure.Rebus;

/// <summary>
/// Имена очередей шины, зафиксированные ADR §10.2 (решение 28): входная очередь шлюза, error-queue
/// и очередь продукта, которая вычисляется из <c>product_id</c> и не задаётся произвольным значением.
/// </summary>
public static partial class QueueNames
{
    /// <summary>Входная очередь шлюза: сюда продукты отправляют <c>OutboundRequest</c>.</summary>
    public const string OutboundQueue = "lti.gateway.outbound";

    /// <summary>Error-queue шлюза: сюда попадают сообщения, не обработанные после всех попыток.</summary>
    public const string ErrorQueue = "lti.gateway.error";

    /// <summary>
    /// Адрес очереди продукта в формате <c>&lt;product_id&gt;.inbox</c>: в неё доставляются
    /// <c>OutboundResult</c> и <c>InboundMessage</c> продукта (ADR §10.2).
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

    [GeneratedRegex("^[a-z][a-z0-9-]{1,31}\\.inbox$", RegexOptions.CultureInvariant)]
    private static partial Regex InboxAddressPattern();
}
