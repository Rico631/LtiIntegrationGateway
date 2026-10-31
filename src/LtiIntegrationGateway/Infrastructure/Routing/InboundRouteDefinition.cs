using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;

namespace LtiIntegrationGateway.Infrastructure.Routing;

/// <summary>
/// Один входящий маршрут: канал, HTTP-метод и шаблон пути из конфигурации, а также настройки
/// ревизии контракта, необходимые для приёма вызова без обращения к базе.
/// </summary>
public sealed record InboundRouteDefinition(
    string ChannelName,
    string HttpMethod,
    string Template,
    Guid? ContractVersionId = null,
    string? OperationId = null,
    string? RecipientQueue = null,
    ChannelAuthType AuthType = ChannelAuthType.None,
    string? ConfigKey = null,
    string? HeaderName = null,
    string? UserName = null,
    int? RateLimitPermit = null,
    int? RateLimitWindowSeconds = null,
    bool Enabled = true);

/// <summary>Набор маршрутов одной ревизии контракта; активируется и деактивируется целиком.</summary>
public sealed record InboundRouteSet(string RevisionKey, IReadOnlyList<InboundRouteDefinition> Routes);

/// <summary>Результат попытки активации набора маршрутов.</summary>
public sealed record InboundRouteActivationResult(IReadOnlyList<string> Problems)
{
    public bool Succeeded => Problems.Count == 0;
}

