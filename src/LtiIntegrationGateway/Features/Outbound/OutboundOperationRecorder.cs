using Google.Protobuf;
using LtiIntegrationGateway.Contracts.Messages.V1;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities;
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

    public async Task CompleteAsync(
        OutboundOperation operation,
        OutboundRequest request,
        OutboundHttpOutcome outcome,
        CancellationToken cancellationToken = default)
    {
        operation.Status = OutboundOperationStatus.Completed;
        operation.Outcome = OutcomeNames.Of(outcome.Outcome);
        operation.HttpStatusCode = outcome.HttpStatusCode;
        operation.ErrorCode = outcome.ErrorCode;
        operation.AttemptCount = outcome.AttemptCount;
        operation.CompletedAt = DateTimeOffset.UtcNow;

        db.OutboundMessageLogs.Add(new OutboundMessageLog
        {
            Id = Guid.NewGuid(),
            MessageId = request.MessageId,
            ProductId = request.ProductId,
            ChannelName = request.ChannelName,
            HttpMethod = request.HttpMethod,
            RelativePath = request.RelativePath,
            RequestBody = request.Body.ToByteArray(),
            ResponseBody = outcome.ResponseBody,
            ContentType = outcome.ContentType ?? request.ContentType,
            HttpStatusCode = outcome.HttpStatusCode,
            Outcome = OutcomeNames.Of(outcome.Outcome),
            ErrorCode = outcome.ErrorCode,
            AttemptCount = outcome.AttemptCount,
            DeliveryStatus = OutboundDeliveryStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
        });

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

        log.DeliveryStatus = OutboundDeliveryStatus.Sent;

        await db.SaveChangesAsync(cancellationToken);
    }
}
