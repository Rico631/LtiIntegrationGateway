using Google.Protobuf;
using LtiIntegrationGateway.Contracts.Messages.V1;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Outbound;

namespace LtiIntegrationGateway.Features.Outbound;

/// <summary>
/// Единственный способ восстановить <see cref="OutboundResult"/> из сохранённых данных: используется
/// повторным запросом и фоновым диспетчером, поэтому обе ветки дают одинаковый результат.
/// </summary>
/// <remarks>
/// Операция хранит исход, статус, код ошибки, число попыток и <c>correlation_id</c>; журнал — тип
/// содержимого, тело ответа и текст ошибки. Запись журнала может отсутствовать при повреждённых
/// данных: тогда результат восстанавливается только из операции.
/// </remarks>
public sealed class StoredOutboundResultFactory
{
    public OutboundResult Create(OutboundOperation operation, OutboundMessageLog? log)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var result = new OutboundResult
        {
            MessageId = operation.MessageId,
            ProductId = operation.ProductId,
            ChannelName = operation.ChannelName,
            Outcome = OutcomeNames.ToOutcome(operation.Outcome),
            ResponseBody = ByteString.CopyFrom(log?.ResponseBody ?? []),
            AttemptCount = operation.AttemptCount,
        };

        if (log?.ContentType is { } contentType)
        {
            result.ContentType = contentType;
        }

        if (log?.ErrorMessage is { } errorMessage)
        {
            result.ErrorMessage = errorMessage;
        }

        if (operation.HttpStatusCode is { } status)
        {
            result.HttpStatusCode = status;
        }

        if (operation.ErrorCode is not null)
        {
            result.ErrorCode = operation.ErrorCode;
        }

        // Поле задаётся всегда, как и в первом результате: иначе пустой correlation_id
        // кодировался бы в ProtoJSON по-разному и повтор переставал быть идентичным.
        result.CorrelationId = operation.CorrelationId ?? string.Empty;

        return result;
    }
}
