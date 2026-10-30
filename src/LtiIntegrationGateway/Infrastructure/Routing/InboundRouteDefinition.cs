namespace LtiIntegrationGateway.Infrastructure.Routing;

/// <summary>Один входящий маршрут: канал, HTTP-метод и шаблон пути из конфигурации.</summary>
public sealed record InboundRouteDefinition(string ChannelName, string HttpMethod, string Template);

/// <summary>Набор маршрутов одной ревизии контракта; активируется и деактивируется целиком.</summary>
public sealed record InboundRouteSet(string RevisionKey, IReadOnlyList<InboundRouteDefinition> Routes);

/// <summary>Результат попытки активации набора маршрутов.</summary>
public sealed record InboundRouteActivationResult(IReadOnlyList<string> Problems)
{
    public bool Succeeded => Problems.Count == 0;
}
