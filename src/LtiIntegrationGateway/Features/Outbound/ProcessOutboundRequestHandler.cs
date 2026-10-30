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
    PublishOutboundResultHandler publisher)
{
    public async Task<ProcessOutboundRequestResult> HandleAsync(
        ProcessOutboundRequestCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;

        var problems = MessageContractValidator.ValidateOutboundRequest(request).ToList();

        if (problems.Count > 0)
        {
            return new ProcessOutboundRequestResult(false, problems);
        }

        var identity = await identityResolver.HandleAsync(
            request.ProductId,
            command.SenderAddress,
            request.ChannelName,
            cancellationToken);

        if (identity.ErrorCode is not null)
        {
            return await RefuseAsync(command, identity.ErrorCode, identity.ErrorMessage, allowUnverifiedAddress: true, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(command.ReturnAddress) ||
            !identity.AllowedAddresses.Contains(command.ReturnAddress, StringComparer.Ordinal))
        {
            return await RefuseAsync(
                command,
                OutboundErrorCodes.ProductNotAllowed,
                "адрес доставки результата не разрешён продукту",
                allowUnverifiedAddress: false,
                cancellationToken);
        }

        var existing = await recorder.FindAsync(request.MessageId, cancellationToken);

        if (existing is not null)
        {
            return await ReplayAsync(existing, command, cancellationToken);
        }

        var channel = await channelLoader.HandleAsync(request.ChannelName, cancellationToken);

        if (channel is null)
        {
            return await RefuseAsync(
                command,
                OutboundErrorCodes.ChannelNotActive,
                $"у канала '{request.ChannelName}' нет активной ревизии",
                allowUnverifiedAddress: true,
                cancellationToken);
        }

        if (!channel.AllowedMethods.Contains(request.HttpMethod, StringComparer.OrdinalIgnoreCase))
        {
            return await RefuseAsync(command, OutboundErrorCodes.MethodNotAllowed, "метод не разрешён каналом", true, cancellationToken);
        }

        if (!RelativePathMatcher.IsStructurallyValid(request.RelativePath) ||
            !channel.AllowedPaths.Any(template => RelativePathMatcher.Matches(request.RelativePath, template)))
        {
            return await RefuseAsync(command, OutboundErrorCodes.PathNotAllowed, "путь не разрешён каналом", true, cancellationToken);
        }

        var authorization = authorizationResolver.Handle(channel);

        if (authorization.ErrorCode is not null)
        {
            return await RefuseAsync(command, authorization.ErrorCode, authorization.ErrorMessage, true, cancellationToken);
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
            AllowRetryForUnsafeMethod(request));

        var outcome = await httpCaller.HandleAsync(httpRequest, cancellationToken);

        await recorder.CompleteAsync(operation, request, outcome, cancellationToken);
        await publisher.HandleAsync(BuildResult(request, outcome), command.ReturnAddress, cancellationToken);

        return new ProcessOutboundRequestResult(true, []);
    }

    private async Task<ProcessOutboundRequestResult> RefuseAsync(
        ProcessOutboundRequestCommand command,
        string errorCode,
        string? errorMessage,
        bool allowUnverifiedAddress,
        CancellationToken cancellationToken)
    {
        var request = command.Request;
        var outcome = OutboundHttpOutcome.Refused(errorCode, errorMessage ?? "запрос отклонён шлюзом");

        var operation = await recorder.FindAsync(request.MessageId, cancellationToken)
                        ?? await recorder.TryStartAsync(request, command.ReturnAddress, cancellationToken);

        if (operation is not null && operation.Status != OutboundOperationStatus.Completed)
        {
            await recorder.CompleteAsync(operation, request, outcome, cancellationToken);
        }

        var address = command.ReturnAddress;

        if (!string.IsNullOrWhiteSpace(address) && (allowUnverifiedAddress || operation is not null))
        {
            await publisher.HandleAsync(BuildResult(request, outcome), address, cancellationToken);
        }

        return new ProcessOutboundRequestResult(false, [$"{errorCode}: {errorMessage}"]);
    }

    private async Task<ProcessOutboundRequestResult> ReplayAsync(
        OutboundOperation operation,
        ProcessOutboundRequestCommand command,
        CancellationToken cancellationToken)
    {
        var request = command.Request;

        if (!string.Equals(operation.ProductId, request.ProductId, StringComparison.Ordinal))
        {
            return await RefuseAsync(
                command,
                OutboundErrorCodes.ProductNotAllowed,
                "операция принадлежит другому продукту",
                allowUnverifiedAddress: false,
                cancellationToken);
        }

        if (operation.Status != OutboundOperationStatus.Completed)
        {
            return new ProcessOutboundRequestResult(true, ["operation: операция уже выполняется"]);
        }

        if (!string.IsNullOrWhiteSpace(command.ReturnAddress))
        {
            await publisher.HandleAsync(
                BuildStoredResult(operation, request),
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

    private static OutboundResult BuildStoredResult(OutboundOperation operation, OutboundRequest request)
    {
        var result = new OutboundResult
        {
            MessageId = operation.MessageId,
            ProductId = operation.ProductId,
            ChannelName = operation.ChannelName,
            Outcome = operation.Outcome switch
            {
                OutcomeNames.HttpResponse => Outcome.HttpResponse,
                OutcomeNames.TransportError => Outcome.TransportError,
                _ => Outcome.GatewayRefused,
            },
            CorrelationId = request.CorrelationId,
            AttemptCount = operation.AttemptCount,
        };

        if (operation.HttpStatusCode is { } status)
        {
            result.HttpStatusCode = status;
        }

        if (operation.ErrorCode is not null)
        {
            result.ErrorCode = operation.ErrorCode;
        }

        return result;
    }

    private static bool AllowRetryForUnsafeMethod(OutboundRequest request)
    {
        var unsafeMethod = request.HttpMethod is "POST" or "PATCH";

        return !unsafeMethod || !string.IsNullOrWhiteSpace(request.IdempotencyKey);
    }
}