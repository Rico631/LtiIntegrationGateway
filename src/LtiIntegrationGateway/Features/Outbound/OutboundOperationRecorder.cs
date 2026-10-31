using LtiIntegrationGateway.Contracts.Messages.V1;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Outbound;
using Microsoft.EntityFrameworkCore;

namespace LtiIntegrationGateway.Features.Outbound;

/// <summary>Имена исходов в терминах контракта.</summary>
public static class OutcomeNames
{
    public const string HttpResponse = "HTTP_RESPONSE";
    public const string TransportError = "TRANSPORT_ERROR";
    public const string GatewayRefused = "GATEWAY_REFUSED";

    public static string Of(Outcome outcome) => outcome switch
    {
        Outcome.HttpResponse => HttpResponse,
        Outcome.TransportError => TransportError,
        _ => GatewayRefused,
    };

    /// <summary>Обратное преобразование сохранённого имени исхода; неизвестное значение — отказ шлюза.</summary>
    public static Outcome ToOutcome(string? name) => name switch
    {
        HttpResponse => Outcome.HttpResponse,
        TransportError => Outcome.TransportError,
        _ => Outcome.GatewayRefused,
    };
}

/// <summary>Состояние операции и журнал: пишутся одной транзакцией до доставки результата.</summary>
public sealed class OutboundOperationRecorder(GatewayDbContext db)
{
    public Task<OutboundOperation?> FindAsync(string messageId, CancellationToken cancellationToken = default) =>
        db.OutboundOperations.SingleOrDefaultAsync(
            operation => operation.MessageId == messageId,
            cancellationToken);

    /// <summary>Журнал пишется один раз на <c>message_id</c>: повторная доставка не создаёт новых вызовов.</summary>
    public Task<OutboundMessageLog?> FindLogAsync(string messageId, CancellationToken cancellationToken = default) =>
        db.OutboundMessageLogs
            .AsNoTracking()
            .FirstOrDefaultAsync(log => log.MessageId == messageId, cancellationToken);

    /// <summary>
    /// Пытается создать операцию. <c>null</c> означает, что операция с таким <c>message_id</c> уже
    /// существует (гонка двух доставок разрешается уникальным индексом).
    /// </summary>
    public async Task<OutboundOperation?> TryStartAsync(
        OutboundRequest request,
        string? returnAddress,
        CancellationToken cancellationToken = default)
    {
        var operation = new OutboundOperation
        {
            Id = Guid.NewGuid(),
            MessageId = request.MessageId,
            ProductId = request.ProductId,
            ChannelName = request.ChannelName,
            Status = OutboundOperationStatus.Accepted,
            AttemptCount = 0,
            ReturnAddress = returnAddress,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.OutboundOperations.Add(operation);

        try
        {
            await db.SaveChangesAsync(cancellationToken);

            return operation;
        }
        catch (DbUpdateException)
        {
            db.Entry(operation).State = EntityState.Detached;

            return null;
        }
    }

    public async Task MarkInProgressAsync(OutboundOperation operation, CancellationToken cancellationToken = default)
    {
        operation.Status = OutboundOperationStatus.InProgress;

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Фиксирует завершение операции и создаёт запись журнала; возвращает созданную запись, чтобы
    /// признак доставки ставился только ей.
    /// </summary>
    public async Task<OutboundMessageLog> CompleteAsync(
        OutboundOperation operation,
        OutboundRequest request,
        string? deliveryAddress,
        int? channelRevisionNumber,
        OutboundHttpOutcome outcome,
        OutboundDeliveryStatus deliveryStatus = OutboundDeliveryStatus.Pending,
        CancellationToken cancellationToken = default)
    {
        var completedAt = DateTimeOffset.UtcNow;

        operation.Status = OutboundOperationStatus.Completed;
        operation.Outcome = OutcomeNames.Of(outcome.Outcome);
        operation.HttpStatusCode = outcome.HttpStatusCode;
        operation.ErrorCode = outcome.ErrorCode;
        operation.CorrelationId = string.IsNullOrWhiteSpace(request.CorrelationId) ? null : request.CorrelationId;
        operation.AttemptCount = outcome.AttemptCount;
        operation.CompletedAt = completedAt;

        var log = new OutboundMessageLog
        {
            Id = Guid.NewGuid(),
            MessageId = request.MessageId,
            ProductId = request.ProductId,
            ChannelName = request.ChannelName,
            DeliveryAddress = deliveryAddress ?? string.Empty,
            HttpMethod = request.HttpMethod,
            RelativePath = request.RelativePath,
            RequestBody = request.Body.ToByteArray(),
            ResponseBody = outcome.ResponseBody,
            // Тип содержимого ответа фиксируется без подстановки типа запроса: повтор результата
            // строится из этой колонки и обязан совпасть с первым результатом.
            ContentType = outcome.ContentType,
            HttpStatusCode = outcome.HttpStatusCode,
            Outcome = OutcomeNames.Of(outcome.Outcome),
            ErrorCode = outcome.ErrorCode,
            ErrorMessage = outcome.ErrorMessage,
            ChannelRevisionNumber = channelRevisionNumber,
            AttemptCount = outcome.AttemptCount,
            DeliveryStatus = deliveryStatus,
            CreatedAt = completedAt,
        };

        db.OutboundMessageLogs.Add(log);

        await db.SaveChangesAsync(cancellationToken);

        return log;
    }

    /// <summary>Фиксирует признак доставки у уже загруженной записи журнала, без повторного чтения.</summary>
    public async Task MarkDeliveredAsync(OutboundMessageLog log, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(log);

        log.DeliveryStatus = OutboundDeliveryStatus.Sent;

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkDeliveredAsync(string messageId, CancellationToken cancellationToken = default)
    {
        var log = await db.OutboundMessageLogs
            .FirstOrDefaultAsync(entity => entity.MessageId == messageId, cancellationToken);

        if (log is null)
        {
            return;
        }

        await MarkDeliveredAsync(log, cancellationToken);
    }
}
