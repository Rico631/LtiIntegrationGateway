namespace LtiIntegrationGateway.Simulation.Product;

/// <summary>
/// Регистрирует конфигурацию продукта в управляющем API до старта отправки событий: без канала,
/// контрактов и маршрута первый же вызов был бы отклонён шлюзом.
/// </summary>
public sealed class ManagementBootstrapHostedService(
    ManagementClient management,
    ILogger<ManagementBootstrapHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Регистрация конфигурации продукта в управляющем API");

        await management.EnsureConfigurationAsync(cancellationToken);

        logger.LogInformation("Конфигурация зарегистрирована, экземпляры интеграции её применили");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
