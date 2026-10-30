using LtiIntegrationGateway.Contracts.Messages.V1;

namespace LtiIntegrationGateway.Contracts;

/// <summary>
/// Проверка обязательных полей интеграционных сообщений до выполнения операции.
/// </summary>
/// <remarks>
/// Возвращает список имён полей с нарушениями, а не исключение: невалидное сообщение —
/// ожидаемый исход, а не сбой процесса.
/// </remarks>
public static class MessageContractValidator
{
    public static IReadOnlyList<string> ValidateOutboundRequest(OutboundRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var violations = new List<string>();

        AddIfEmpty(violations, request.MessageId, "message_id");
        AddIfEmpty(violations, request.ProductId, "product_id");
        AddIfEmpty(violations, request.ChannelName, "channel_name");
        AddIfEmpty(violations, request.RelativePath, "relative_path");
        AddIfEmpty(violations, request.HttpMethod, "http_method");

        if (request.Body.Length > 0 && string.IsNullOrWhiteSpace(request.ContentType))
        {
            violations.Add("content_type");
        }

        return violations;
    }

    private static void AddIfEmpty(List<string> violations, string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            violations.Add(fieldName);
        }
    }
}
