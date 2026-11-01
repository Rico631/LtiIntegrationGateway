namespace LtiIntegrationGateway.Simulation.Integration;

/// <summary>Настройки симуляции внешней системы.</summary>
public sealed record IntegrationOptions(string GatewayBaseUrl)
{
    /// <summary>Читает настройки и падает при отсутствии обязательного ключа.</summary>
    public static IntegrationOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return configuration["Gateway:BaseUrl"] is { Length: > 0 } baseUrl
            ? new IntegrationOptions(baseUrl)
            : throw new InvalidOperationException("Ключ 'Gateway:BaseUrl' обязателен.");
    }
}
