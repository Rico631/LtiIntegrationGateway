using LtiIntegrationGateway.Infrastructure.Observability;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Outbound;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LtiIntegrationGateway.Features.Outbound;

/// <summary>
/// Фоновая служба повторной доставки результатов, зафиксированных со статусом <c>pending</c>.
/// </summary>
/// <remarks>
/// Свойства (ADR §8.9):
/// <list type="bullet">
/// <item><c>Send</c> и смена статуса не объединены транзакцией: падение между ними даёт дубль, но не
/// потерю — гарантия доставки «минимум один раз»;</item>
/// <item>пачка обрабатывается последовательно, ошибка одной записи не прерывает остальные;</item>
/// <item>распределённой блокировки нет: при нескольких экземплярах приложения дубли допустимы, продукт
/// дедуплицирует результат по <c>message_id</c> (ADR §12, §20);</item>
/// <item>в лог попадают <c>messageId</c>, канал и причина, без тел и секретов.</item>
/// </list>
/// </remarks>
public sealed class OutboundResultDispatcher(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboundDeliveryOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboundResultDispatcher> logger,
    GatewayMetrics metrics) : BackgroundService
{
    private readonly OutboundDeliveryOptions _options = CreateOptions(options);

    /// <summary>Цикл службы: обработка пачки и пауза до следующего запуска.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("диспетчер результатов отключён конфигурацией");

            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // Следующий цикл повторит выборку: сбой чтения или записи в БД не должен
                // останавливать службу.
                logger.LogError(exception, "цикл диспетчера результатов завершился ошибкой");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.IntervalSeconds), timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Обрабатывает одну пачку недоставленных результатов: восстановление через
    /// <see cref="StoredOutboundResultFactory"/>, повторная отправка и признак доставки.
    /// </summary>
    public async Task DispatchPendingAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
        var recorder = scope.ServiceProvider.GetRequiredService<OutboundOperationRecorder>();
        var resultFactory = scope.ServiceProvider.GetRequiredService<StoredOutboundResultFactory>();
        var publisher = scope.ServiceProvider.GetRequiredService<PublishOutboundResultHandler>();

        // Порядок обеспечивается колонкой CreatedAt: EF хранит DateTimeOffset как миллисекунды Unix.
        // Записи без адреса доставки и без операции не участвуют в выборке: они не могут быть
        // доставлены и не должны занимать пачку, иначе доставляемые записи голодают.
        var batch = await db.PendingForDelivery()
            .OrderBy(log => log.CreatedAt)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken);

        if (batch.Count == 0)
        {
            return;
        }

        var messageIds = batch.Select(log => log.MessageId).ToArray();

        var operations = (await db.OutboundOperations
                .Where(operation => messageIds.Contains(operation.MessageId))
                .ToListAsync(cancellationToken))
            .ToDictionary(operation => operation.MessageId, StringComparer.Ordinal);

        foreach (var log in batch)
        {
            cancellationToken.ThrowIfCancellationRequested();

            operations.TryGetValue(log.MessageId, out var operation);

            await DispatchAsync(db, recorder, resultFactory, publisher, log, operation, cancellationToken);
        }
    }

    private async Task DispatchAsync(
        GatewayDbContext db,
        OutboundOperationRecorder recorder,
        StoredOutboundResultFactory resultFactory,
        PublishOutboundResultHandler publisher,
        OutboundMessageLog log,
        OutboundOperation? operation,
        CancellationToken cancellationToken)
    {
        if (operation is null)
        {
            // Защита от повреждённых данных: такие записи не попадают в выборку и остаются pending.
            logger.LogWarning(
                "результат {MessageId} канала {ChannelName} не отправлен: операция не найдена",
                log.MessageId,
                log.ChannelName);

            return;
        }

        if (string.IsNullOrWhiteSpace(log.DeliveryAddress))
        {
            // Защита от повреждённых данных: пустой адрес исключён из выборки.
            logger.LogWarning(
                "результат {MessageId} канала {ChannelName} не отправлен: адрес доставки пуст",
                log.MessageId,
                log.ChannelName);

            return;
        }

        using var activity = GatewayTelemetry.StartActivity(GatewayTelemetry.OutboundDeliveryActivity);
        GatewayTelemetry.SetTag(activity, GatewayTelemetry.DirectionTag, GatewayTelemetry.OutboundDirection);
        GatewayTelemetry.SetTag(activity, GatewayTelemetry.MessageIdTag, log.MessageId);
        GatewayTelemetry.SetTag(activity, GatewayTelemetry.ChannelTag, log.ChannelName);

        try
        {
            await publisher.SendAsync(resultFactory.Create(operation, log), log.DeliveryAddress);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            GatewayTelemetry.SetTag(activity, GatewayTelemetry.OutcomeTag, "failed");
            GatewayTelemetry.SetTag(activity, GatewayTelemetry.ErrorCodeTag, "delivery");
            GatewayTelemetry.SetTag(activity, GatewayTelemetry.DeliverySucceededTag, false);
            metrics.RecordDelivery(log.ChannelName, succeeded: false);

            // Отправка не удалась: запись остаётся pending, следующий цикл повторит попытку.
            logger.LogWarning(
                exception,
                "отправка результата {MessageId} канала {ChannelName} не удалась; запись остаётся в ожидании",
                log.MessageId,
                log.ChannelName);

            return;
        }

        GatewayTelemetry.SetTag(activity, GatewayTelemetry.OutcomeTag, "delivered");
        GatewayTelemetry.SetTag(activity, GatewayTelemetry.DeliverySucceededTag, true);
        metrics.RecordDelivery(log.ChannelName, succeeded: true);

        try
        {
            await recorder.MarkDeliveredAsync(log, cancellationToken);

            logger.LogInformation(
                "результат {MessageId} канала {ChannelName} доставлен повторно",
                log.MessageId,
                log.ChannelName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Сообщение уже ушло продукту: сбой фиксации приводит к дублю при следующем цикле,
            // но не к потере результата.
            logger.LogError(
                exception,
                "результат {MessageId} канала {ChannelName} отправлен, но статус доставки не " +
                "зафиксирован — возможен дубль",
                log.MessageId,
                log.ChannelName);

            await TryReloadAsync(db, log, cancellationToken);
        }
    }

    /// <summary>Возвращает сущность к сохранённому состоянию, чтобы следующий цикл снова её отправил.</summary>
    private async Task TryReloadAsync(
        GatewayDbContext db,
        OutboundMessageLog log,
        CancellationToken cancellationToken)
    {
        try
        {
            await db.Entry(log).ReloadAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "не удалось перечитать запись {MessageId} после сбоя фиксации статуса",
                log.MessageId);
        }
    }

    private static OutboundDeliveryOptions CreateOptions(IOptions<OutboundDeliveryOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var value = options.Value;
        value.Validate();

        return value;
    }
}
