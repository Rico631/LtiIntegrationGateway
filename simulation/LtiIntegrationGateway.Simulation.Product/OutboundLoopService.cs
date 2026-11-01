using System.Text.Json;
using Google.Protobuf;
using LtiIntegrationGateway.Contracts.Messages.V1;
using LtiIntegrationGateway.Infrastructure.Rebus;
using Rebus.Bus;
using Rebus.Messages;

namespace LtiIntegrationGateway.Simulation.Product;

/// <summary>Раз в 5 секунд публикует исходящий вызов <c>POST /customer</c> внешней системы.</summary>
public sealed class OutboundLoopService(
    IBus bus,
    ProductSimulationOptions options,
    ILogger<OutboundLoopService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    private int _counter;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var number = Interlocked.Increment(ref _counter);

            try
            {
                await SendAsync(number, stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Не удалось отправить OutboundRequest");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task SendAsync(int number, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.Serialize(new { name = $"customer-{number}" });

        var request = new OutboundRequest
        {
            MessageId = Guid.NewGuid().ToString("N"),
            ProductId = options.ProductId,
            ChannelName = ProductSimulationOptions.OutboundChannel,
            RelativePath = "/customer",
            HttpMethod = "POST",
            ContentType = "application/json",
            Body = ByteString.CopyFromUtf8(body),
            IdempotencyKey = Guid.NewGuid().ToString("N"),
        };

        var headers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Headers.ReturnAddress] = QueueNames.ProductInbox(options.ProductId),
        };

        await bus.Advanced.Routing.Send(
            QueueNames.OutboundQueue(options.Integration),
            request,
            headers);

        logger.LogInformation(
            "POST /customer -> шлюз: messageId={MessageId}, body={Body}",
            request.MessageId,
            body);
    }
}
