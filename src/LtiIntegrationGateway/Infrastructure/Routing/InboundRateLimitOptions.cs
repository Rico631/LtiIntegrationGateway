namespace LtiIntegrationGateway.Infrastructure.Routing;

/// <summary>Имя политики ограничения частоты для динамических входящих маршрутов.</summary>
public static class InboundRateLimit
{
    public const string PolicyName = "inbound-per-channel";
}

/// <summary>Квота частоты входящих вызовов: окно и число запросов на канал.</summary>
public sealed class InboundRateLimitOptions
{
    public const string SectionName = "Inbound:RateLimit";

    public int PermitLimit { get; set; } = 100;

    public int WindowSeconds { get; set; } = 60;
}
