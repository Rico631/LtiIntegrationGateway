using System.Security.Cryptography.X509Certificates;
using LtiIntegrationGateway.Contracts.Messages.V1;
using LtiIntegrationGateway.Infrastructure.Http;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;

namespace LtiIntegrationGateway.Features.Outbound;

/// <summary>Данные одного исходящего HTTP-вызова: адрес, тело, авторизация и политика повторов.</summary>
public sealed record OutboundHttpRequest(
    string Method,
    string BaseUrl,
    string RelativePath,
    IReadOnlyList<(string Name, string Value)> QueryParameters,
    string? ContentType,
    byte[] Body,
    IReadOnlyDictionary<string, string> AuthorizationHeaders,
    RetryPolicy RetryPolicy,
    int PerAttemptTimeoutMs,
    int OverallCapMs,
    bool AllowRetryForUnsafeMethod,
    ChannelAuthType AuthType,
    X509Certificate2? ClientCertificate);

/// <summary>Фактический исход вызова: HTTP-ответ, транспортная ошибка или отказ шлюза.</summary>
public sealed record OutboundHttpOutcome(
    Outcome Outcome,
    int? HttpStatusCode,
    byte[] ResponseBody,
    string? ContentType,
    string? ErrorCode,
    string? ErrorMessage,
    int AttemptCount)
{
    public static OutboundHttpOutcome HttpResponse(int status, byte[] body, string? contentType, int attempts) =>
        new(Outcome.HttpResponse, status, body, contentType, null, null, attempts);

    public static OutboundHttpOutcome Transport(string errorCode, string message, int attempts) =>
        new(Outcome.TransportError, null, [], null, errorCode, message, attempts);

    public static OutboundHttpOutcome Refused(string errorCode, string message) =>
        new(Outcome.GatewayRefused, null, [], null, errorCode, message, 0);
}

/// <summary>Выполнение HTTP-вызова через обработчик пула с повторами по политике канала.</summary>
public sealed class ExecuteHttpCallHandler(IChannelHttpClientFactory httpClientFactory)
{
    public async Task<OutboundHttpOutcome> HandleAsync(
        OutboundHttpRequest request,
        CancellationToken cancellationToken = default)
    {
        var key = ChannelHttpClientKey.From(request.BaseUrl, request.AuthType, request.ClientCertificate);

        using var lease = httpClientFactory.Acquire(key, request.ClientCertificate);
        using var client = new HttpClient(lease.Handler, disposeHandler: false);

        var uri = BuildUri(request);
        var startedAt = DateTimeOffset.UtcNow;
        var attempts = 0;

        while (true)
        {
            attempts++;

            using var attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptCancellation.CancelAfter(TimeSpan.FromMilliseconds(request.PerAttemptTimeoutMs));

            try
            {
                using var message = BuildMessage(request, uri);
                using var response = await client.SendAsync(
                    message,
                    HttpCompletionOption.ResponseContentRead,
                    attemptCancellation.Token);

                var status = (int)response.StatusCode;
                var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                var contentType = response.Content.Headers.ContentType?.MediaType;

                var delay = DecideStatusRetry(request, status, attempts, startedAt, GetRetryAfter(response));

                if (delay is null)
                {
                    return OutboundHttpOutcome.HttpResponse(status, body, contentType, attempts);
                }

                await Task.Delay(delay.Value, cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                var delay = DecideTransportRetry(request, attempts, startedAt);

                if (delay is null)
                {
                    return ToTransportOutcome(OutboundErrorCodes.Timeout, "истёк таймаут попытки", attempts);
                }

                await Task.Delay(delay.Value, cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                var code = IsTlsFailure(exception)
                    ? OutboundErrorCodes.TlsFailed
                    : OutboundErrorCodes.ConnectionFailed;

                var delay = DecideTransportRetry(request, attempts, startedAt);

                if (delay is null)
                {
                    return ToTransportOutcome(code, "не удалось выполнить запрос", attempts);
                }

                await Task.Delay(delay.Value, cancellationToken);
            }
        }
    }

    private static OutboundHttpOutcome ToTransportOutcome(string errorCode, string message, int attempts) =>
        attempts > 1
            ? OutboundHttpOutcome.Transport(OutboundErrorCodes.RetriesExhausted, "повторы исчерпаны", attempts)
            : OutboundHttpOutcome.Transport(errorCode, message, attempts);

    private static bool IsTlsFailure(HttpRequestException exception) =>
        exception.InnerException is System.Security.Authentication.AuthenticationException;

    private static Uri BuildUri(OutboundHttpRequest request)
    {
        var baseUrl = request.BaseUrl.EndsWith('/') ? request.BaseUrl : request.BaseUrl + "/";
        var baseUri = new Uri(baseUrl, UriKind.Absolute);
        var uri = new Uri(baseUri, request.RelativePath.TrimStart('/'));

        if (request.QueryParameters.Count == 0)
        {
            return uri;
        }

        var query = string.Join(
            "&",
            request.QueryParameters.Select(parameter =>
                $"{Uri.EscapeDataString(parameter.Name)}={Uri.EscapeDataString(parameter.Value)}"));

        return new Uri(uri + "?" + query);
    }

    private static HttpRequestMessage BuildMessage(OutboundHttpRequest request, Uri uri)
    {
        var message = new HttpRequestMessage(new HttpMethod(request.Method), uri);

        if (request.Body.Length > 0)
        {
            message.Content = new ByteArrayContent(request.Body);

            if (!string.IsNullOrWhiteSpace(request.ContentType) &&
                System.Net.Http.Headers.MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaType))
            {
                message.Content.Headers.ContentType = mediaType;
            }
        }

        foreach (var header in request.AuthorizationHeaders)
        {
            message.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return message;
    }

    private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;

        if (retryAfter?.Delta is { } delta)
        {
            return delta;
        }

        if (retryAfter?.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;

            return wait > TimeSpan.Zero ? wait : null;
        }

        return null;
    }

    private static TimeSpan? DecideStatusRetry(
        OutboundHttpRequest request,
        int status,
        int attempts,
        DateTimeOffset startedAt,
        TimeSpan? retryAfter)
    {
        if (!request.AllowRetryForUnsafeMethod)
        {
            return null;
        }

        var rule = request.RetryPolicy.Rules.FirstOrDefault(candidate =>
            candidate.Match == RetryMatchType.HttpStatusIn && candidate.StatusCodes.Contains(status));

        return rule is null ? null : NextDelay(rule, attempts, startedAt, request.OverallCapMs, retryAfter);
    }

    private static TimeSpan? DecideTransportRetry(
        OutboundHttpRequest request,
        int attempts,
        DateTimeOffset startedAt)
    {
        if (!request.AllowRetryForUnsafeMethod)
        {
            return null;
        }

        var rule = request.RetryPolicy.Rules.FirstOrDefault(candidate =>
            candidate.Match == RetryMatchType.TransportError);

        return rule is null ? null : NextDelay(rule, attempts, startedAt, request.OverallCapMs, null);
    }

    private static TimeSpan? NextDelay(
        RetryRule rule,
        int attempts,
        DateTimeOffset startedAt,
        int overallCapMs,
        TimeSpan? retryAfter)
    {
        var retryIndex = attempts - 1;

        if (retryIndex < 0 || retryIndex >= rule.DelaysMs.Count)
        {
            return null;
        }

        var delay = TimeSpan.FromMilliseconds(rule.DelaysMs[retryIndex]);

        if (retryAfter is { } serverDelay && serverDelay > delay)
        {
            delay = serverDelay;
        }

        var deadline = startedAt + TimeSpan.FromMilliseconds(overallCapMs);

        return DateTimeOffset.UtcNow + delay > deadline ? null : delay;
    }
}