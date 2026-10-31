using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Inbound;
using Microsoft.EntityFrameworkCore;

namespace LtiIntegrationGateway.Features.Inbound;

/// <summary>
/// Запись журнала входящих вызовов. Каждая операция выполняется в собственном scope, поэтому
/// обработчик приёма остаётся singleton и не удерживает <see cref="GatewayDbContext"/> между запросами.
/// </summary>
public sealed class InboundMessageLogWriter(IServiceScopeFactory scopeFactory, ILogger<InboundMessageLogWriter> logger)
{
    /// <summary>Добавляет запись журнала и возвращает её идентификатор либо <c>null</c> при сбое.</summary>
    public async Task<Guid?> AppendAsync(
        Func<InboundMessageLog> entryFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entryFactory);

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
            var entry = entryFactory();

            db.InboundMessageLogs.Add(entry);
            await db.SaveChangesAsync(cancellationToken);

            return entry.Id;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Не удалось записать входящий вызов в журнал");

            return null;
        }
    }

    /// <summary>Обновляет статус передачи записи; сбой обновления не прерывает обработку вызова.</summary>
    public async Task UpdateStatusAsync(
        Guid? entryId,
        InboundDeliveryStatus status,
        string? errorCode,
        CancellationToken cancellationToken = default)
    {
        if (entryId is null)
        {
            return;
        }

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();

            await db.InboundMessageLogs
                .Where(entry => entry.Id == entryId)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(entry => entry.DeliveryStatus, status)
                        .SetProperty(entry => entry.ErrorCode, errorCode),
                    cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Не удалось обновить статус входящего вызова в журнале");
        }
    }
}
