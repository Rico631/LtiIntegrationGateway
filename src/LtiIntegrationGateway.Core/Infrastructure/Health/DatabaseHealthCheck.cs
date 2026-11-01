using LtiIntegrationGateway.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LtiIntegrationGateway.Infrastructure.Health;

/// <summary>Проверка доступности хранилища для readiness-пробы.</summary>
public sealed class DatabaseHealthCheck(IServiceScopeFactory scopeFactory) : IHealthCheck
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();

            return await db.Database.CanConnectAsync(timeout.Token)
                ? HealthCheckResult.Healthy("storage available")
                : HealthCheckResult.Unhealthy("storage unavailable");
        }
        catch (OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("storage unavailable");
        }
        catch
        {
            // Текст исключения может содержать строку подключения; наружу отдаётся только статус.
            return HealthCheckResult.Unhealthy("storage unavailable");
        }
    }
}
