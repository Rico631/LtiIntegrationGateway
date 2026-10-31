using LtiIntegrationGateway.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LtiIntegrationGateway.Infrastructure.Observability;

/// <summary>Периодически обновляет gauge количества результатов, ожидающих доставки.</summary>
public sealed class OutboundBacklogMetricsUpdater(
    IServiceScopeFactory scopeFactory,
    GatewayMetrics metrics,
    TimeProvider timeProvider,
    ILogger<OutboundBacklogMetricsUpdater> logger) : BackgroundService
{
    /// <summary>Интервал обновления gauge.</summary>
    public static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(15);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await UpdateAsync(stoppingToken);

            try
            {
                await Task.Delay(UpdateInterval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Считывает backlog и обновляет gauge; ошибка БД не останавливает приложение.</summary>
    public async Task UpdateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
            var pending = await db.PendingForDelivery().LongCountAsync(cancellationToken);

            metrics.SetPending(pending);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "не удалось обновить gauge ожидающих результатов");
        }
    }
}
