using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Google.Protobuf.WellKnownTypes;
using LtiIntegrationGateway.Contracts.Messages.V1;
using LtiIntegrationGateway.Infrastructure.OpenApi;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities;
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
    ILogger<InboundRequestHandler> logger)
{
    /// <summary>Максимальное количество query-параметров в запросе.</summary>
    public const int MaxQueryParameters = 64;

    /// <summary>Максимальная глубина JSON в теле запроса.</summary>
    public const int MaxJsonDepth = 64;

    public async Task HandleAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var route = context.GetEndpoint()?.Metadata.GetMetadata<InboundRouteMetadata>();

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
        };

        if (!string.IsNullOrWhiteSpace(route.OperationId))
        {
            message.OperationId = route.OperationId;
        }

        message.QueryParameters.AddRange(queryParameters);

        var entryId = await journal.AppendAsync(
            () => CreateEntry(message, route, template, body, queryParameters, InboundDeliveryStatus.Pending, null, receivedAt),
            context.RequestAborted);

        if (entryId is null)
        {
            await WriteStatusAsync(context, StatusCodes.Status503ServiceUnavailable);

            return;
        }

        try
        {
            await publisher.PublishAsync(route.RecipientQueue, message, context.RequestAborted);

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

            await WriteStatusAsync(context, StatusCodes.Status503ServiceUnavailable);
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
}
