using LtiIntegrationGateway.Features.Configuration;
using LtiIntegrationGateway.Features.Inbound;
using LtiIntegrationGateway.Messaging;
using Microsoft.Extensions.Options;
using Rebus.Activation;
using Rebus.Bus;
using Rebus.Config;
using Rebus.Retry.Simple;

namespace LtiIntegrationGateway.Infrastructure.Rebus;

/// <summary>
/// Reply-шина экземпляра: принимает <c>InboundResponse</c> на очереди
/// <see cref="QueueNames.ReplyQueue(string, string)"/> и завершает ожидание экземпляра, принявшего
/// синхронный вызов.
/// </summary>
/// <remarks>
/// Отдельная шина нужна потому, что один Rebus-экземпляр потребляет одну входную очередь. Основная
/// шина читает очередь интеграции, reply-шина — очередь экземпляра; ответ продукта адресуется
/// заголовком <c>rbs2-return-address</c> и не попадает к другой реплике.
/// </remarks>
public sealed class FlowReplyBusHost(
    IConfiguration configuration,
    IOptions<FlowOptions> options,
    PendingInboundRequests pending,
    ProtobufSerializer serializer,
    ILoggerFactory loggerFactory) : IHostedService
{
    private IBus? _bus;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var flow = options.Value;

        flow.Validate();

        var connectionString = configuration.GetConnectionString("Gateway")
            ?? throw new InvalidOperationException(
                "Строка подключения к хранилищу не настроена. " +
                "Задайте ключ конфигурации 'ConnectionStrings:Gateway'.");

        var activator = new BuiltinHandlerActivator();

        activator.Register(() => new InboundResponseHandler(
            pending,
            loggerFactory.CreateLogger<InboundResponseHandler>()));

        _bus = Configure.With(activator)
            .Transport(transport => transport.UsePostgreSql(
                connectionString,
                tableName: QueueNames.MessageTable,
                inputQueueName: QueueNames.ReplyQueue(flow.Integration, flow.InstanceId)))
            .Options(configurer => configurer.RetryStrategy(
                errorQueueName: QueueNames.ErrorQueue(flow.Integration)))
            .Serialization(serialization => serialization.Register(_ => serializer))
            .Start();

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _bus?.Dispose();
        _bus = null;

        return Task.CompletedTask;
    }
}
