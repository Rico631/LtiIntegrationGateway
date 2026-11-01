using System.Diagnostics;
using Google.Protobuf;
using LtiIntegrationGateway.Contracts;
using LtiIntegrationGateway.Contracts.Messages.V1;
using LtiIntegrationGateway.Infrastructure.Observability;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Outbound;

namespace LtiIntegrationGateway.Features.Outbound;

/// <summary>Запрос в обработку исходящего контура вместе с адресами из заголовков сообщения.</summary>
public sealed record ProcessOutboundRequestCommand(
    OutboundRequest Request,
    string? SenderAddress,
    string? ReturnAddress);

public sealed record ProcessOutboundRequestResult(bool Completed, IReadOnlyList<string> Problems);

/// <summary>Состояние телеметрии операции, не зависящее от сэмплирования traces.</summary>
internal sealed class OutboundTelemetryState
{
    /// <summary>Исход операции.</summary>
    public string Outcome { get; set; } = OutcomeNames.GatewayRefused;

    /// <summary>Безопасный код ошибки.</summary>
    public string? ErrorCode { get; set; }

    /// <summary>Количество выполненных попыток.</summary>
    public int AttemptCount { get; set; }
}

/// <summary>Обработка исходящего запроса: проверки, вызов, фиксация и доставка результата.</summary>
public sealed class ProcessOutboundRequestHandler(
    ResolveSenderIdentityHandler identityResolver,
    LoadActiveChannelHandler channelLoader,
    ResolveAuthorizationHandler authorizationResolver,
    ExecuteHttpCallHandler httpCaller,
    OutboundOperationRecorder recorder,
    StoredOutboundResultFactory resultFactory,
    PublishOutboundResultHandler publisher,
    GatewayMetrics metrics)
{
    private static readonly AsyncLocal<OutboundTelemetryState?> CurrentTelemetry = new();

    public async Task<ProcessOutboundRequestResult> HandleAsync(
        ProcessOutboundRequestCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var stopwatch = Stopwatch.StartNew();
        using var activity = GatewayTelemetry.StartActivity(GatewayTelemetry.OutboundOperationActivity);
        var telemetry = new OutboundTelemetryState();

        GatewayTelemetry.SetTag(activity, GatewayTelemetry.DirectionTag, GatewayTelemetry.OutboundDirection);
        GatewayTelemetry.SetTag(activity, GatewayTelemetry.MessageIdTag, command.Request.MessageId);
        GatewayTelemetry.SetTag(activity, GatewayTelemetry.CorrelationIdTag, command.Request.CorrelationId);
        GatewayTelemetry.SetTag(activity, GatewayTelemetry.ProductIdTag, command.Request.ProductId);
        GatewayTelemetry.SetTag(activity, GatewayTelemetry.ChannelTag, command.Request.ChannelName);
        GatewayTelemetry.SetTag(activity, GatewayTelemetry.HttpMethodTag, command.Request.HttpMethod);
        GatewayTelemetry.SetTag(activity, GatewayTelemetry.UrlPathTag, command.Request.RelativePath);

        CurrentTelemetry.Value = telemetry;
        ProcessOutboundRequestResult result;

        try
        {
            result = await HandleCoreAsync(command, telemetry, cancellationToken);
        }
        finally
        {
            CurrentTelemetry.Value = null;
        }

        metrics.RecordOutbound(
            command.Request.ProductId,
            command.Request.ChannelName,
            telemetry.Outcome,
            telemetry.ErrorCode,
            stopwatch.Elapsed.TotalMilliseconds,
            telemetry.AttemptCount);

        return result;
    }

    private async Task<ProcessOutboundRequestResult> HandleCoreAsync(
        ProcessOutboundRequestCommand command,
        OutboundTelemetryState telemetry,
        CancellationToken cancellationToken)
    {
        var request = command.Request;

        var problems = MessageContractValidator.ValidateOutboundRequest(request);

        if (problems.Count > 0)
        {
            telemetry.Outcome = OutcomeNames.GatewayRefused;
            telemetry.ErrorCode = "validation";
            telemetry.AttemptCount = 0;

            GatewayTelemetry.SetTag(Activity.Current, GatewayTelemetry.OutcomeTag, OutcomeNames.GatewayRefused);
            GatewayTelemetry.SetTag(Activity.Current, GatewayTelemetry.ErrorCodeTag, "validation");
            GatewayTelemetry.SetTag(Activity.Current, GatewayTelemetry.AttemptCountTag, 0);

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

        if (!identity.IsSuccess)
        {
            return await RefuseAsync(
                command,
                identity.ErrorCode!,
                identity.ErrorMessage,
                deliverResult: false,
                existing,
                cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(command.ReturnAddress) ||
            !identity.Value!.AllowedAddresses.Contains(command.ReturnAddress, StringComparer.Ordinal))
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
            // Канал с таким именем в другой интеграции — не «нет активной ревизии», а чужой канал.
            var foreign = await channelLoader.ExistsInOtherIntegrationAsync(
                request.ChannelName,
                cancellationToken);

            return await RefuseAsync(
                command,
                foreign ? OutboundErrorCodes.ChannelNotAllowed : OutboundErrorCodes.ChannelNotActive,
                foreign
                    ? $"канал '{request.ChannelName}' принадлежит другой интеграции"
                    : $"у канала '{request.ChannelName}' нет активной ревизии",
                deliverResult: true,
                existing,
                cancellationToken,
                telemetry: telemetry);
        }

        if (!RelativePathValidator.IsStructurallyValid(request.RelativePath))
        {
            return await RefuseAsync(
                command,
                OutboundErrorCodes.PathNotAllowed,
                "путь недопустим",
                deliverResult: true,
                existing,
                cancellationToken,
                channelRevisionNumber: channel.Number);
        }

        var authorization = authorizationResolver.Handle(channel);

        if (!authorization.IsSuccess)
        {
            return await RefuseAsync(
                command,
                authorization.ErrorCode!,
                authorization.ErrorMessage,
                deliverResult: true,
                existing,
                cancellationToken,
                channelRevisionNumber: channel.Number);
        }

        var authorizationData = authorization.Value!;

        var operation = await recorder.TryStartAsync(request, command.ReturnAddress, cancellationToken);

        if (operation is null)
        {
            var raced = await recorder.FindAsync(request.MessageId, cancellationToken);

            if (raced is null)
            {
                telemetry.Outcome = OutcomeNames.GatewayRefused;
                telemetry.ErrorCode = "operation";
                telemetry.AttemptCount = 0;

                GatewayTelemetry.SetTag(Activity.Current, GatewayTelemetry.OutcomeTag, OutcomeNames.GatewayRefused);
                GatewayTelemetry.SetTag(Activity.Current, GatewayTelemetry.ErrorCodeTag, "operation");
                GatewayTelemetry.SetTag(Activity.Current, GatewayTelemetry.AttemptCountTag, 0);

                return new ProcessOutboundRequestResult(false, ["operation: не удалось создать операцию"]);
            }

            return await ReplayAsync(raced, command, cancellationToken, telemetry);
        }

        await recorder.MarkInProgressAsync(operation, cancellationToken);

        var httpRequest = new OutboundHttpRequest(
            request.HttpMethod,
            channel.BaseUrl,
            request.RelativePath,
            request.QueryParameters
                .Select(parameter => (parameter.Name, parameter.Value))
                .Concat(authorizationData.QueryParameters)
                .ToArray(),
            request.ContentType,
            request.Body.ToByteArray(),
            authorizationData.Headers,
            channel.RetryPolicy,
            channel.PerAttemptTimeoutMs,
            channel.OverallCapMs,
            AllowRetryForUnsafeMethod(request),
            channel.AuthType,
            authorizationData.ClientCertificate);

        var outcome = await httpCaller.HandleAsync(httpRequest, cancellationToken);

        GatewayTelemetry.SetTag(Activity.Current, GatewayTelemetry.OutcomeTag, OutcomeNames.Of(outcome.Outcome));
        GatewayTelemetry.SetTag(Activity.Current, GatewayTelemetry.ErrorCodeTag, outcome.ErrorCode);
        GatewayTelemetry.SetTag(Activity.Current, GatewayTelemetry.HttpStatusCodeTag, outcome.HttpStatusCode);
        GatewayTelemetry.SetTag(Activity.Current, GatewayTelemetry.AttemptCountTag, outcome.AttemptCount);
        GatewayTelemetry.SetTag(Activity.Current, GatewayTelemetry.ChannelRevisionTag, channel.Number);

        telemetry.Outcome = OutcomeNames.Of(outcome.Outcome);
        telemetry.ErrorCode = outcome.ErrorCode;
        telemetry.AttemptCount = outcome.AttemptCount;

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
        int? channelRevisionNumber = null,
        OutboundTelemetryState? telemetry = null)
    {
        var request = command.Request;
        var outcome = OutboundHttpOutcome.Refused(errorCode, errorMessage ?? "запрос отклонён шлюзом");
        var acceptsResult = deliverResult && !string.IsNullOrWhiteSpace(command.ReturnAddress);
        var address = acceptsResult ? command.ReturnAddress : null;
        var state = telemetry ?? CurrentTelemetry.Value;

        if (state is not null)
        {
            state.Outcome = OutcomeNames.GatewayRefused;
            state.ErrorCode = errorCode;
            state.AttemptCount = 0;
        }

        GatewayTelemetry.SetTag(Activity.Current, GatewayTelemetry.OutcomeTag, OutcomeNames.GatewayRefused);
        GatewayTelemetry.SetTag(Activity.Current, GatewayTelemetry.ErrorCodeTag, errorCode);
        GatewayTelemetry.SetTag(Activity.Current, GatewayTelemetry.AttemptCountTag, 0);
        GatewayTelemetry.SetTag(Activity.Current, GatewayTelemetry.ChannelRevisionTag, channelRevisionNumber);

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
        CancellationToken cancellationToken,
        OutboundTelemetryState? telemetry = null)
    {
        var state = telemetry ?? CurrentTelemetry.Value;

        if (state is not null)
        {
            state.Outcome = operation.Outcome ?? "IN_PROGRESS";
            state.AttemptCount = operation.AttemptCount;
        }

        GatewayTelemetry.SetTag(
            Activity.Current,
            GatewayTelemetry.OutcomeTag,
            operation.Outcome ?? "IN_PROGRESS");
        GatewayTelemetry.SetTag(Activity.Current, GatewayTelemetry.AttemptCountTag, operation.AttemptCount);

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
