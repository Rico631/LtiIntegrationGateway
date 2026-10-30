using System.Net;
using System.Text;

namespace LtiIntegrationGateway.Tests.TestSupport;

/// <summary>Запрос, зафиксированный тестовым обработчиком.</summary>
public sealed record RecordedHttpRequest(string Method, string Uri, IReadOnlyDictionary<string, string> Headers);

/// <summary>
/// HTTP-обработчик, возвращающий заранее заданную последовательность ответов и ошибок и
/// записывающий фактически отправленные запросы.
/// </summary>
public sealed class RecordingHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responders = new();
    private readonly List<RecordedHttpRequest> _requests = [];

    public IReadOnlyList<RecordedHttpRequest> Requests => _requests;

    public int CallCount => _requests.Count;

    public void EnqueueResponse(HttpStatusCode status, string body = "{}", string contentType = "application/json")
    {
        _responders.Enqueue(_ =>
        {
            var content = new StringContent(body, Encoding.UTF8);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            content.Headers.ContentType.CharSet = null;

            return new HttpResponseMessage(status) { Content = content };
        });
    }

    public void EnqueueException(Exception exception) =>
        _responders.Enqueue(_ => throw exception);

    public void EnqueueResponseWithRetryAfter(HttpStatusCode status, TimeSpan retryAfter)
    {
        _responders.Enqueue(_ => new HttpResponseMessage(status)
        {
            Headers = { RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(retryAfter) },
        });
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        _requests.Add(new RecordedHttpRequest(
            request.Method.Method,
            request.RequestUri?.ToString() ?? string.Empty,
            request.Headers.ToDictionary(header => header.Key, header => string.Join(",", header.Value), StringComparer.Ordinal)));

        if (_responders.Count == 0)
        {
            throw new InvalidOperationException("тестовый обработчик не получил заготовленный ответ");
        }

        return Task.FromResult(_responders.Dequeue()(request));
    }
}

/// <summary>Фабрика клиентов, всегда отдающая клиент с одним тестовым обработчиком.</summary>
public sealed class StubHttpClientFactory(RecordingHttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}
