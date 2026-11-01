namespace LtiIntegrationGateway.Simulation.Integration;

/// <summary>
/// Раз в 5 секунд вызывает шлюз по входящему контуру: <c>POST /order</c> (асинхронный канал)
/// и <c>GET /order/{orderid}</c> (синхронный канал), и пишет в лог ответ.
/// </summary>
public sealed class IntegrationLoopService(
    IntegrationOptions options,
    IHttpClientFactory httpClientFactory,
    ILogger<IntegrationLoopService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    private int _counter;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var client = httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(options.GatewayBaseUrl);

        while (!stoppingToken.IsCancellationRequested)
        {
            var number = Interlocked.Increment(ref _counter);
            var orderId = $"order-{number}";

            await SendAsync(
                client,
                new HttpRequestMessage(HttpMethod.Post, "/order")
                {
                    Content = JsonContent.Create(new { orderId, amount = number * 10 }),
                },
                $"POST /order ({orderId})",
                stoppingToken);

            await SendAsync(
                client,
                new HttpRequestMessage(HttpMethod.Get, $"/order/{orderId}"),
                $"GET /order/{orderId}",
                stoppingToken);

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task SendAsync(
        HttpClient client,
        HttpRequestMessage request,
        string description,
        CancellationToken cancellationToken)
    {
        try
        {
            using (request)
            {
                using var response = await client.SendAsync(request, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                logger.LogInformation(
                    "{Call} -> {Status}: {Body}",
                    description,
                    (int)response.StatusCode,
                    string.IsNullOrWhiteSpace(body) ? "<пусто>" : body);
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "{Call} завершился ошибкой", description);
        }
    }
}
