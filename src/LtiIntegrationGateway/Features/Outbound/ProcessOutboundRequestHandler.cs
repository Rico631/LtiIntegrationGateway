using Google.Protobuf;
using LtiIntegrationGateway.Contracts;
using LtiIntegrationGateway.Contracts.Messages.V1;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities;

namespace LtiIntegrationGateway.Features.Outbound;

/// <summary>Запрос в обработку исходящего контура вместе с адресами из заголовков сообщения.</summary>
public sealed record ProcessOutboundRequestCommand(
    OutboundRequest Request,
    string? SenderAddress,
    string? ReturnAddress);

public sealed record ProcessOutboundRequestResult(bool Completed, IReadOnlyList<string> Problems);

/// <summary>Обработка исходящего запроса: проверки, вызов, фиксация и доставка результата.</summary>
public sealed class ProcessOutboundRequestHandler(
    ResolveSenderIdentityHandler identityResolver,
    LoadActiveChannelHandler channelLoader,
    ResolveAuthorizationHandler authorizationResolver,
    ExecuteHttpCallHandler httpCaller,
    OutboundOperationRecorder recorder,
    StoredOutboundResultFactory resultFactory,
    PublishOutboundResultHandler publisher)
{
    public async Task<ProcessOutboundRequestResult> HandleAsync(
        ProcessOutboundRequestCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;

        var problems = MessageContractValidator.ValidateOutboundRequest(request);

        if (problems.Count > 0)
        {
            return new ProcessOutboundRequestResult(false, problems);
        }

        // Идемпотентность проверяется до правил канала: состояние операции и владение решают,
        // будет ли выполнен внешний вызов (ADR §8.10).
        var existing = await recorder.FindAsync(request.MessageId, cancellationToken);

        if (existing is not null &&
            !string.Equals(existing.ProductId, request.ProductId, StringComparison.Ordinal))
        {
            // Чужой продукт не получает сохранённый результат, а состояние операции не меняется.
            return await RefuseAsync(
                command,
                OutboundErrorCodes.ProductNotAllowed,
                "операция принадлежит другому продукту",
                deliverResult: false,
                existing,
                cancellationToken);
        }

        var identity = await identityResolver.HandleAsync(
            request.ProductId,
            command.SenderAddress,
            request.ChannelName,
            cancellationToken);

        if (identity.ErrorCode is not null)
        {
            return await RefuseAsync(
                command,
                identity.ErrorCode,
                identity.ErrorMessage,
                deliverResult: false,
                existing,
                cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(command.ReturnAddress) ||
            !identity.AllowedAddresses.Contains(command.ReturnAddress, StringComparer.Ordinal))
        {
            return await RefuseAsync(
                command,
                OutboundErrorCodes.ProductNotAllowed,
                "адрес доставки результата не разрешён продукту",
                deliverResult: false,
                existing,
                cancellationToken);
        }

        if (existing is not null)
        {
            // Завершённая операция отдаёт сохранённый результат: канал, метод, путь и авторизация
            // повторно не проверяются, второй внешний вызов не выполняется.
            return await ReplayAsync(existing, command, cancellationToken);
        }

        var channel = await channelLoader.HandleAsync(request.ChannelName, cancellationToken);

        if (channel is null)
        {
            return await RefuseAsync(
                command,
                OutboundErrorCodes.ChannelNotActive,
                $"у канала '{request.ChannelName}' нет активной ревизии",
                deliverResult: true,
                existing,
                cancellationToken);
        }

        if (!channel.AllowedMethods.Contains(request.HttpMethod, StringComparer.OrdinalIgnoreCase))
        {
            return await RefuseAsync(
                command,
                OutboundErrorCodes.MethodNotAllowed,
                "метод не разрешён каналом",
                deliverResult: true,
                existing,
                cancellationToken,
                channelRevisionNumber: channel.Number);
        }

        if (!RelativePathMatcher.IsStructurallyValid(request.RelativePath) ||
            !channel.AllowedPaths.Any(template => RelativePathMatcher.Matches(request.RelativePath, template)))
        {
            return await RefuseAsync(
                command,
                OutboundErrorCodes.PathNotAllowed,
                "путь не разрешён каналом",
                deliverResult: true,
                existing,
                cancellationToken,
                channelRevisionNumber: channel.Number);
        }

        var authorization = authorizationResolver.Handle(channel);

        if (authorization.ErrorCode is not null)
        {
            return await RefuseAsync(
                command,
                authorization.ErrorCode,
                authorization.ErrorMessage,
                deliverResult: true,
                existing,
                cancellationToken,
                channelRevisionNumber: channel.Number);
        }

        var operation = await recorder.TryStartAsync(request, command.ReturnAddress, cancellationToken);

        if (operation is null)
        {
            var raced = await recorder.FindAsync(request.MessageId, cancellationToken);

            return raced is null
                ? new ProcessOutboundRequestResult(false, ["operation: не удалось создать операцию"])
                : await ReplayAsync(raced, command, cancellationToken);
        }

        await recorder.MarkInProgressAsync(operation, cancellationToken);

        var httpRequest = new OutboundHttpRequest(
            request.HttpMethod,
            channel.BaseUrl,
            request.RelativePath,
            request.QueryParameters
                .Select(parameter => (parameter.Name, parameter.Value))
                .Concat(authorization.QueryParameters)
                .ToArray(),
            request.ContentType,
            request.Body.ToByteArray(),
            authorization.Headers,
            channel.RetryPolicy,
            channel.PerAttemptTimeoutMs,
            channel.OverallCapMs,
            AllowRetryForUnsafeMethod(request),
            channel.AuthType,
            authorization.ClientCertificate);

        var outcome = await httpCaller.HandleAsync(httpRequest, cancellationToken);

        var log = await recorder.CompleteAsync(
            operation,
            request,
            command.ReturnAddress,
            channel.Number,
            outcome,
            cancellationToken: cancellationToken);

        await publisher.HandleAsync(BuildResult(request, outcome), command.ReturnAddress, log, cancellationToken);

        return new ProcessOutboundRequestResult(true, []);
    }

    private async Task<ProcessOutboundRequestResult> RefuseAsync(
        ProcessOutboundRequestCommand command,
        string errorCode,
        string? errorMessage,
        bool deliverResult,
        OutboundOperation? existing,
        CancellationToken cancellationToken,
        int? channelRevisionNumber = null)
    {
        var request = command.Request;
        var outcome = OutboundHttpOutcome.Refused(errorCode, errorMessage ?? "запрос отклонён шлюзом");
        var acceptsResult = deliverResult && !string.IsNullOrWhiteSpace(command.ReturnAddress);
        var address = acceptsResult ? command.ReturnAddress : null;

        var operation = existing;
        OutboundMessageLog? createdLog = null;

        if (operation is null)
        {
            // Операции не было: отказ фиксируется в журнале как единственный результат запроса.
            operation = await recorder.TryStartAsync(request, address, cancellationToken);

            if (operation is not null)
            {
                createdLog = await recorder.CompleteAsync(
                    operation,
                    request,
                    address,
                    channelRevisionNumber,
                    outcome,
                    acceptsResult ? OutboundDeliveryStatus.Pending : OutboundDeliveryStatus.Undeliverable,
                    cancellationToken);
            }
            else
            {
                // Гонка: операцию создал параллельный запрос — её состояние не меняется.
                operation = await recorder.FindAsync(request.MessageId, cancellationToken);
            }
        }

        // Завершённая операция принадлежит первому результату: журнал и статус не меняются, а
        // сохранённый результат не подменяется свежим отказом.
        if (acceptsResult)
        {
            var result = BuildResult(request, outcome);

            if (createdLog is not null)
            {
                await publisher.HandleAsync(result, address!, createdLog, cancellationToken);
            }
            else
            {
                // Отказ не создавал записи журнала: чужая или уже завершённая операция остаётся
                // в своём статусе, иначе сохранённый результат владельца потерялся бы.
                await publisher.SendAsync(result, address!);
            }
        }

        return new ProcessOutboundRequestResult(false, [$"{errorCode}: {errorMessage}"]);
    }

    private async Task<ProcessOutboundRequestResult> ReplayAsync(
        OutboundOperation operation,
        ProcessOutboundRequestCommand command,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(operation.ProductId, command.Request.ProductId, StringComparison.Ordinal))
        {
            // Гонка двух продуктов с одним message_id: сохранённый результат не отдаётся.
            return await RefuseAsync(
                command,
                OutboundErrorCodes.ProductNotAllowed,
                "операция принадлежит другому продукту",
                deliverResult: false,
                operation,
                cancellationToken);
        }

        if (operation.Status != OutboundOperationStatus.Completed)
        {
            // Второй вызов не запускается: результат придёт один раз по завершении первой операции.
            return new ProcessOutboundRequestResult(true, ["operation: операция уже выполняется"]);
        }

        if (!string.IsNullOrWhiteSpace(command.ReturnAddress))
        {
            var log = await recorder.FindLogAsync(operation.MessageId, cancellationToken);

            await publisher.HandleAsync(
                resultFactory.Create(operation, log),
                command.ReturnAddress,
                cancellationToken);
        }

        return new ProcessOutboundRequestResult(true, []);
    }

    private static OutboundResult BuildResult(OutboundRequest request, OutboundHttpOutcome outcome)
    {
        var result = new OutboundResult
        {
            MessageId = request.MessageId,
            ProductId = request.ProductId,
            ChannelName = request.ChannelName,
            Outcome = outcome.Outcome,
            ResponseBody = ByteString.CopyFrom(outcome.ResponseBody),
            CorrelationId = request.CorrelationId,
            AttemptCount = outcome.AttemptCount,
        };

        if (outcome.HttpStatusCode is { } status)
        {
            result.HttpStatusCode = status;
        }

        if (outcome.ContentType is not null)
        {
            result.ContentType = outcome.ContentType;
        }

        if (outcome.ErrorCode is not null)
        {
            result.ErrorCode = outcome.ErrorCode;
        }

        if (outcome.ErrorMessage is not null)
        {
            result.ErrorMessage = outcome.ErrorMessage;
        }

        return result;
    }

    private static bool AllowRetryForUnsafeMethod(OutboundRequest request)
    {
        var unsafeMethod = request.HttpMethod is "POST" or "PATCH";

        return !unsafeMethod || !string.IsNullOrWhiteSpace(request.IdempotencyKey);
    }
}
