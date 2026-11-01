namespace LtiIntegrationGateway.Simulation.Product;

/// <summary>Настройки симуляции продукта из конфигурации приложения.</summary>
public sealed record ProductSimulationOptions(
    string Integration,
    string ProductId,
    string IntegrationBaseUrl,
    string ManagementBaseUrl,
    string ManagementApiKey,
    string ManagementHeaderName)
{
    /// <summary>Имя outbound-канала, который регистрируется в управляющем API.</summary>
    /// <remarks>Имя уникально внутри интеграции, поэтому префикс интеграции не нужен: канал описывает транспорт.</remarks>
    public const string OutboundChannel = "http";

    /// <summary>Читает настройки и падает при отсутствии обязательного ключа.</summary>
    public static ProductSimulationOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return new ProductSimulationOptions(
            Required(configuration, "Gateway:Integration"),
            Required(configuration, "Gateway:ProductId"),
            Required(configuration, "Integration:BaseUrl"),
            Required(configuration, "Management:BaseUrl"),
            Required(configuration, "Management:ApiKey"),
            configuration["Management:HeaderName"] ?? "X-Management-Api-Key");
    }

    private static string Required(IConfiguration configuration, string key) =>
        configuration[key] is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Ключ '{key}' обязателен.");
}
