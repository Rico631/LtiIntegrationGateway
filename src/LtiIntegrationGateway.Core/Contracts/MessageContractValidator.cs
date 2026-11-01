using LtiIntegrationGateway.Contracts.Management.V1;
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

    public static IReadOnlyList<string> ValidateCreateChannel(string name, ChannelSettings? settings)
    {
        var violations = new List<string>();

        AddRequired(violations, name, "name", "имя канала обязательно");

        if (settings is null)
        {
            violations.Add("settings: настройки канала обязательны");
        }

        return violations;
    }

    public static IReadOnlyList<string> ValidateUpdateChannelDraft(string channelName, ChannelSettings? settings)
    {
        var violations = new List<string>();

        AddRequired(violations, channelName, "channel_name", "имя канала обязательно");

        if (settings is null)
        {
            violations.Add("settings: настройки канала обязательны");
        }

        return violations;
    }

    public static IReadOnlyList<string> ValidateChannelRevisionCommand(string channelName, int revision)
    {
        var violations = new List<string>();

        AddRequired(violations, channelName, "channel_name", "имя канала обязательно");

        if (revision <= 0)
        {
            violations.Add("revision: номер ревизии должен быть положительным");
        }

        return violations;
    }

    public static IReadOnlyList<string> ValidateRollbackChannelRevision(string channelName)
    {
        var violations = new List<string>();

        AddRequired(violations, channelName, "channel_name", "имя канала обязательно");

        return violations;
    }

    public static IReadOnlyList<string> ValidateRegisterProductRoute(
        string productId,
        IReadOnlyCollection<string> allowedChannels)
    {
        var violations = new List<string>();

        AddRequired(violations, productId, "product_id", "идентификатор продукта обязателен");

        if (allowedChannels.Count == 0)
        {
            violations.Add("allowed_channels: должен быть указан хотя бы один канал");
        }

        return violations;
    }

    public static IReadOnlyList<string> ValidateUploadContract(string channelName, string document)
    {
        var violations = new List<string>();

        AddRequired(violations, channelName, "channel_name", "имя канала обязательно");

        if (string.IsNullOrWhiteSpace(document))
        {
            violations.Add("document: документ контракта обязателен");
        }

        return violations;
    }

    public static IReadOnlyList<string> ValidateContractVersionCommand(string channelName, int version)
    {
        var violations = new List<string>();

        AddRequired(violations, channelName, "channel_name", "имя канала обязательно");

        if (version <= 0)
        {
            violations.Add("version: номер версии должен быть положительным");
        }

        return violations;
    }

    public static IReadOnlyList<string> ValidateUpdateContractVersionSettings(
        string channelName,
        int version,
        ContractVersionSettings? settings)
    {
        var violations = ValidateContractVersionCommand(channelName, version).ToList();

        if (settings is null)
        {
            violations.Add("settings: настройки версии обязательны");
        }

        return violations;
    }

    public static IReadOnlyList<string> ValidateUpdateContractOperationSettings(
        string channelName,
        int version,
        string operationId)
    {
        var violations = ValidateContractVersionCommand(channelName, version).ToList();

        AddRequired(violations, operationId, "operation_id", "идентификатор операции обязателен");

        return violations;
    }

    private static void AddRequired(List<string> violations, string value, string fieldName, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            violations.Add($"{fieldName}: {message}");
        }
    }

    private static void AddIfEmpty(List<string> violations, string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            violations.Add(fieldName);
        }
    }
}
