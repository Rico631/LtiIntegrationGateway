using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Google.Protobuf.WellKnownTypes;
using LtiIntegrationGateway.Contracts.Messages.V1;
using LtiIntegrationGateway.Infrastructure.Observability;
using LtiIntegrationGateway.Infrastructure.OpenApi;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Inbound;
using LtiIntegrationGateway.Infrastructure.Routing;

namespace LtiIntegrationGateway.Features.Inbound;

/// <summary>
/// Обработчик входящего вызова активной операции контракта: проверки запроса в фиксированном порядке,
/// формирование <see cref="InboundMessage"/>, журнал и передача в очередь получателя.
/// </summary>
public sealed class InboundRequestHandler(
    InboundRequestAuthenticator authenticator,
    InboundSchemaCache schemaCache,
    InboundMessageLogWriter journal,
    IInboundMessagePublisher publisher,
    ILogger<InboundRequestHandler> logger,
    GatewayMetrics metrics)
{
    private const string ErrorCodeItemKey = "LtiIntegrationGateway.Telemetry.ErrorCode";

    /// <summary>Максимальное количество query-параметров в запросе.</summary>
    public const int MaxQueryParameters = 64;

    /// <summary>Максимальная глубина JSON в теле запроса.</summary>
    public const int MaxJsonDepth = 64;

    /// <summary>Обрабатывает входящий вызов и записывает безопасную телеметрию операции.</summary>
    public async Task HandleAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var route = context.GetEndpoint()?.Metadata.GetMetadata<InboundRouteMetadata>();

        if (route is null)
        {
            await HandleCoreAsync(context, null);

            return;
        }

        var method = context.Request.Method;
        var template = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? string.Empty;
        var started = Stopwatch.GetTimestamp();

        using var activity = GatewayTelemetry.StartActivity(GatewayTelemetry.InboundRequestActivity);
        GatewayTelemetry.SetTag(activity, GatewayTelemetry.DirectionTag, GatewayTelemetry.InboundDirection);
        GatewayTelemetry.SetTag(activity, GatewayTelemetry.ChannelTag, route.ChannelName);
        GatewayTelemetry.SetTag(activity, GatewayTelemetry.HttpMethodTag, method);
        GatewayTelemetry.SetTag(activity, GatewayTelemetry.HttpRouteTag, template);
        GatewayTelemetry.SetTag(activity, GatewayTelemetry.UrlPathTag, InboundRequestPath.Get(context));
        GatewayTelemetry.SetTag(activity, GatewayTelemetry.OperationIdTag, route.OperationId);
        GatewayTelemetry.SetTag(
            activity,
            GatewayTelemetry.InboundResponseModeTag,
            route.ResponseMode == InboundResponseMode.Sync ? "sync" : "async");

        try
        {
            await HandleCoreAsync(context, route);
        }
        finally
        {
            var statusCode = context.Response.StatusCode;
            var errorCode = context.Items.TryGetValue(ErrorCodeItemKey, out var item) ? item as string : null;

            GatewayTelemetry.SetTag(activity, GatewayTelemetry.HttpStatusCodeTag, statusCode);

            metrics.RecordInbound(
                route.ChannelName,
                method,
                template,
                statusCode,
                errorCode,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    private async Task HandleCoreAsync(HttpContext context, InboundRouteMetadata? route)
    {
        route ??= context.GetEndpoint()?.Metadata.GetMetadata<InboundRouteMetadata>();

        if (route is null)
        {
            logger.LogError("Динамический входящий маршрут без метаданных: {Path}", InboundRequestPath.Get(context));

            await WriteStatusAsync(context, StatusCodes.Status500InternalServerError);

            return;
        }

        var request = context.Request;
        var template = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? string.Empty;
        var queryParameters = CollectQuery(request);
        var body = await ReadBodyAsync(request, context.RequestAborted);

        if (queryParameters.Count > MaxQueryParameters)
        {
            await RejectAsync(context, route, template, body, queryParameters, StatusCodes.Status400BadRequest, "query: превышено количество параметров");

            return;
        }

        var contentType = NormalizeMediaType(request.ContentType);
        var schema = ResolveSchema(route, request.Method, template);

        if (body.Length > 0 && schema is null)
        {
            await RejectAsync(context, route, template, body, queryParameters, StatusCodes.Status400BadRequest, "body: схема тела не объявлена");

            return;
        }

        if (body.Length > 0 && contentType is null)
        {
            await RejectAsync(context, route, template, body, queryParameters, StatusCodes.Status415UnsupportedMediaType, "content_type: тип содержимого не указан");

            return;
        }

        if (schema is not null && contentType is not null && !schema.ContentSchemas.ContainsKey(contentType))
        {
            await RejectAsync(context, route, template, body, queryParameters, StatusCodes.Status415UnsupportedMediaType, "content_type: тип содержимого не поддерживается");

            return;
        }

        var authentication = authenticator.Authenticate(context, route);

        if (authentication == InboundAuthenticationResult.SecretNotConfigured)
        {
            await RejectAsync(context, route, template, body, queryParameters, StatusCodes.Status503ServiceUnavailable, "auth: секрет не настроен");

            return;
        }

        if (authentication == InboundAuthenticationResult.Unauthorized)
        {
            await RejectAsync(context, route, template, body, queryParameters, StatusCodes.Status401Unauthorized, "auth: аутентификация не пройдена");

            return;
        }

        JsonDocument? json = null;

        if (body.Length > 0 && IsJsonContentType(contentType))
        {
            try
            {
                json = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = MaxJsonDepth });
            }
            catch (JsonException)
            {
                await RejectAsync(context, route, template, body, queryParameters, StatusCodes.Status400BadRequest, "body: JSON не разбирается или слишком глубокий");

                return;
            }
        }

        using (json)
        {
            if (json is not null && schema is not null && contentType is not null)
            {
                var validationErrors = schema.ContentSchemas[contentType].Validate(json.RootElement.GetRawText());

                if (validationErrors.Count > 0)
                {
                    logger.LogWarning(
                        "Входящий вызов отклонён схемой: канал {Channel}, маршрут {Template}, ошибок {Count}",
                        route.ChannelName,
                        template,
                        validationErrors.Count);

                    await RejectAsync(context, route, template, body, queryParameters, StatusCodes.Status422UnprocessableEntity, "schema: тело не соответствует схеме");

                    return;
                }
            }

            await DispatchAsync(context, route, template, body, contentType, queryParameters);
        }
    }

    private async Task DispatchAsync(
        HttpContext context,
        InboundRouteMetadata route,
        string template,
        byte[] body,
        string? contentType,
        IReadOnlyList<QueryParameter> queryParameters)
    {
        if (string.IsNullOrWhiteSpace(route.RecipientQueue))
        {
            await RejectAsync(context, route, template, body, queryParameters, StatusCodes.Status503ServiceUnavailable, "queue: очередь получателя не настроена");

            return;
        }

        var recipientQueue = route.RecipientQueue;

        var receivedAt = DateTimeOffset.UtcNow;
        var message = new InboundMessage
        {
            MessageId = Guid.NewGuid().ToString("N"),
            ChannelName = route.ChannelName,
            HttpMethod = context.Request.Method,
            RelativePath = InboundRequestPath.Get(context),
            RouteTemplate = template,
            ContentType = contentType ?? string.Empty,
            Body = Google.Protobuf.ByteString.CopyFrom(body),
            ReceivedAt = Timestamp.FromDateTimeOffset(receivedAt),
            ReplyExpected = route.ResponseMode == InboundResponseMode.Sync,
        };

        if (!string.IsNullOrWhiteSpace(route.OperationId))
        {
            message.OperationId = route.OperationId;
        }

        message.QueryParameters.AddRange(queryParameters);

        var entryId = await journal.AppendAsync(
            () => CreateEntry(message, route, template, body, queryParameters, InboundDeliveryStatus.Pending, null, receivedAt),
            CancellationToken.None);

        if (entryId is null)
        {
            SetErrorCode(context, "storage");

            await WriteStatusAsync(context, StatusCodes.Status503ServiceUnavailable);

            return;
        }

        if (route.ResponseMode == InboundResponseMode.Async)
        {
            await PublishAsync(context, route, message, entryId.Value, template);

            return;
        }

        await HandleSynchronousAsync(context, route, message, entryId.Value, template);
    }

    private async Task PublishAsync(
        HttpContext context,
        InboundRouteMetadata route,
        InboundMessage message,
        Guid entryId,
        string template)
    {
        try
        {
            await publisher.PublishAsync(route.RecipientQueue!, message, context.RequestAborted);

            await journal.UpdateStatusAsync(entryId, InboundDeliveryStatus.Sent, null, CancellationToken.None);

            await WriteStatusAsync(context, StatusCodes.Status202Accepted);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "Не удалось передать входящий вызов в очередь {Queue}: канал {Channel}, маршрут {Template}",
                route.RecipientQueue,
                route.ChannelName,
                template);

            await journal.UpdateStatusAsync(entryId, InboundDeliveryStatus.Failed, "delivery", CancellationToken.None);
            SetErrorCode(context, "delivery");

            await WriteStatusAsync(context, StatusCodes.Status503ServiceUnavailable);
        }
    }

    /// <summary>
    /// Ожидание ответа в синхронном режиме: ответ продукта проверяется fail-closed и
    /// переносится в HTTP-ответ; клиентская отмена не превращается в ответ.
    /// </summary>
    private async Task HandleSynchronousAsync(
        HttpContext context,
        InboundRouteMetadata route,
        InboundMessage message,
        Guid entryId,
        string template)
    {
        var timeout = TimeSpan.FromMilliseconds(Math.Max(1, route.InboundResponseTimeoutMs));

        try
        {
            var response = await publisher.SendRequestAsync(
                route.RecipientQueue!,
                message,
                timeout,
                context.RequestAborted);

            var problems = ValidateResponse(message.MessageId, response);

            if (problems.Count > 0)
            {
                await journal.UpdateResponseAsync(
                    entryId,
                    InboundDeliveryStatus.Failed,
                    response.HttpStatusCode is >= 200 and <= 599 ? response.HttpStatusCode : null,
                    response.ContentType,
                    response.Body.ToByteArray(),
                    SerializeResponseHeaders(response),
                    "invalid_inbound_response",
                    CancellationToken.None);

                SetErrorCode(context, "invalid_inbound_response");
                logger.LogWarning(
                    "Некорректный InboundResponse: канал {Channel}, маршрут {Template}, проблемы {Problems}",
                    route.ChannelName,
                    template,
                    string.Join("; ", problems));

                await WriteStatusAsync(context, StatusCodes.Status502BadGateway);

                return;
            }

            await journal.UpdateResponseAsync(
                entryId,
                InboundDeliveryStatus.Replied,
                response.HttpStatusCode,
                string.IsNullOrWhiteSpace(response.ContentType) ? null : response.ContentType,
                response.Body.ToByteArray(),
                SerializeResponseHeaders(response),
                string.IsNullOrWhiteSpace(response.ErrorCode) ? null : response.ErrorCode,
                CancellationToken.None);

            await WriteInboundResponseAsync(context, response);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            await journal.UpdateResponseAsync(
                entryId,
                InboundDeliveryStatus.Cancelled,
                null,
                null,
                [],
                null,
                "cancelled",
                CancellationToken.None);
        }
        catch (TimeoutException)
        {
            await HandleTimeoutAsync(context, route, entryId, template);
        }
        catch (OperationCanceledException)
        {
            await HandleTimeoutAsync(context, route, entryId, template);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Не удалось выполнить синхронный входящий вызов: канал {Channel}, маршрут {Template}",
                route.ChannelName,
                template);

            await journal.UpdateResponseAsync(
                entryId,
                InboundDeliveryStatus.Failed,
                null,
                null,
                [],
                null,
                "delivery",
                CancellationToken.None);

            SetErrorCode(context, "delivery");

            await WriteStatusAsync(context, StatusCodes.Status503ServiceUnavailable);
        }
    }

    private async Task HandleTimeoutAsync(
        HttpContext context,
        InboundRouteMetadata route,
        Guid entryId,
        string template)
    {
        logger.LogWarning(
            "Истёк таймаут InboundResponse: канал {Channel}, маршрут {Template}, timeout {TimeoutMs} мс",
            route.ChannelName,
            template,
            route.InboundResponseTimeoutMs);

        await journal.UpdateResponseAsync(
            entryId,
            InboundDeliveryStatus.TimedOut,
            null,
            null,
            [],
            null,
            "inbound_timeout",
            CancellationToken.None);

        SetErrorCode(context, "inbound_timeout");

        await WriteStatusAsync(context, StatusCodes.Status504GatewayTimeout);
    }

    private static readonly HashSet<string> AllowedResponseHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Location",
        "Retry-After",
        "Cache-Control",
        "ETag",
        "Last-Modified",
        "Vary",
        "WWW-Authenticate",
    };

    private static IReadOnlyList<string> ValidateResponse(string messageId, InboundResponse response)
    {
        var problems = new List<string>();

        if (!string.Equals(response.MessageId, messageId, StringComparison.Ordinal))
        {
            problems.Add("message_id: не совпадает с исходным сообщением");
        }

        if (response.HttpStatusCode is < 200 or > 299 &&
            response.HttpStatusCode is < 400 or > 599)
        {
            problems.Add("http_status_code: допустимы только 200..299 или 400..599");
        }

        if (response.Body.Length > 0 && string.IsNullOrWhiteSpace(response.ContentType))
        {
            problems.Add("content_type: обязателен при непустом body");
        }

        if (!string.IsNullOrWhiteSpace(response.ContentType) &&
            !MediaTypeHeaderValue.TryParse(response.ContentType, out _))
        {
            problems.Add("content_type: некорректный media type");
        }

        foreach (var header in response.Headers)
        {
            if (!AllowedResponseHeaders.Contains(header.Name))
            {
                problems.Add($"headers: заголовок '{header.Name}' не разрешён");
            }

            if (header.Name.Contains('\r') || header.Name.Contains('\n') ||
                header.Value.Contains('\r') || header.Value.Contains('\n'))
            {
                problems.Add($"headers: недопустимые символы в заголовке '{header.Name}'");
            }
        }

        return problems;
    }

    private static string? SerializeResponseHeaders(InboundResponse response) =>
        response.Headers.Count == 0
            ? null
            : JsonSerializer.Serialize(response.Headers.Select(header => new { name = header.Name, value = header.Value }));

    private static async Task WriteInboundResponseAsync(HttpContext context, InboundResponse response)
    {
        context.Response.StatusCode = response.HttpStatusCode;

        if (!string.IsNullOrWhiteSpace(response.ContentType))
        {
            context.Response.ContentType = response.ContentType;
        }

        foreach (var header in response.Headers)
        {
            context.Response.Headers.Append(header.Name, header.Value);
        }

        if (response.Body.Length > 0)
        {
            await context.Response.Body.WriteAsync(response.Body.Memory, context.RequestAborted);
        }
    }

    private async Task RejectAsync(
        HttpContext context,
        InboundRouteMetadata route,
        string template,
        byte[] body,
        IReadOnlyList<QueryParameter> queryParameters,
        int statusCode,
        string errorCode)
    {
        SetErrorCode(context, SafeErrorCode(errorCode));

        await journal.AppendAsync(
            () => CreateEntry(
                message: null,
                route,
                template,
                body,
                queryParameters,
                InboundDeliveryStatus.Failed,
                errorCode,
                DateTimeOffset.UtcNow,
                context),
            context.RequestAborted);

        await WriteStatusAsync(context, statusCode);
    }

    private static InboundMessageLog CreateEntry(
        InboundMessage? message,
        InboundRouteMetadata route,
        string template,
        byte[] body,
        IReadOnlyList<QueryParameter> queryParameters,
        InboundDeliveryStatus status,
        string? errorCode,
        DateTimeOffset receivedAt,
        HttpContext? context = null)
    {
        var method = message?.HttpMethod ?? context?.Request.Method ?? string.Empty;
        var path = message?.RelativePath ?? InboundRequestPath.Get(context!);

        return new InboundMessageLog
        {
            Id = Guid.NewGuid(),
            MessageId = message?.MessageId ?? Guid.NewGuid().ToString("N"),
            ChannelName = route.ChannelName,
            ContractVersionId = route.ContractVersionId,
            HttpMethod = method,
            RelativePath = path,
            RouteTemplate = template,
            OperationId = route.OperationId,
            ContentType = message?.ContentType ?? NormalizeMediaType(context?.Request.ContentType),
            RequestBody = body,
            QueryParameters = queryParameters.Count == 0 ? null : SerializeQuery(queryParameters),
            DeliveryStatus = status,
            ErrorCode = errorCode,
            ReceivedAt = receivedAt,
        };
    }

    private static string SerializeQuery(IReadOnlyList<QueryParameter> queryParameters)
    {
        var array = new JsonArray();

        foreach (var parameter in queryParameters)
        {
            array.Add(new JsonObject
            {
                ["name"] = parameter.Name,
                ["value"] = parameter.Value,
            });
        }

        return array.ToJsonString();
    }

    private static IReadOnlyList<QueryParameter> CollectQuery(HttpRequest request) =>
        request.Query
            .SelectMany(pair => pair.Value.Select(value => new QueryParameter { Name = pair.Key, Value = value ?? string.Empty }))
            .ToArray();

    private InboundOperationSchema? ResolveSchema(InboundRouteMetadata route, string method, string template) =>
        route.ContractVersionId is { } versionId
            ? schemaCache.Find(versionId, method, template)
            : null;

    private static async Task<byte[]> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentLength == 0)
        {
            return [];
        }

        using var buffer = new MemoryStream();

        await request.Body.CopyToAsync(buffer, cancellationToken);

        return buffer.ToArray();
    }

    private static bool IsJsonContentType(string? contentType) =>
        contentType is not null &&
        (contentType.Equals("application/json", StringComparison.OrdinalIgnoreCase) ||
            contentType.EndsWith("+json", StringComparison.OrdinalIgnoreCase));

    private static string? NormalizeMediaType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType) || !MediaTypeHeaderValue.TryParse(contentType, out var parsed))
        {
            return null;
        }

        return parsed.MediaType?.Trim().ToLowerInvariant();
    }

    private static Task WriteStatusAsync(HttpContext context, int statusCode)
    {
        context.Response.StatusCode = statusCode;

        return Task.CompletedTask;
    }

    private static string SafeErrorCode(string errorCode)
    {
        var separator = errorCode.IndexOf(':', StringComparison.Ordinal);

        return separator < 0 ? errorCode : errorCode[..separator];
    }

    private static void SetErrorCode(HttpContext context, string errorCode)
    {
        context.Items[ErrorCodeItemKey] = errorCode;
        GatewayTelemetry.SetTag(Activity.Current, GatewayTelemetry.ErrorCodeTag, errorCode);
    }
}
