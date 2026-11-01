using Rebus.Config;
using Rebus.Retry.Simple;
using Rebus.Transport.InMem;

namespace LtiIntegrationGateway.Infrastructure.Rebus;

/// <summary>
/// Подключение шины шлюза: транспорт, входная очередь, error-queue и сериализатор.
/// </summary>
public static class RebusConfigurationExtensions
{
    public static IServiceCollection AddGatewayRebus(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ProtobufMessageRegistry>();

        var registry = new ProtobufMessageRegistry();
        var serializer = new ProtobufSerializer(registry);

        services.AddSingleton(registry);
        services.AddSingleton(serializer);

        services.AddRebus(
            configure => configure
                .Transport(transport => transport.UseInMemoryTransport(new InMemNetwork(), QueueNames.OutboundQueue))
                .Options(ConfigureRetryStrategy)
                .Serialization(serialization => serialization.Register(_ => serializer)));

        return services;
    }

    /// <summary>
    /// Настройка повторов шины: сообщение, не обработанное после всех попыток, попадает в
    /// <see cref="QueueNames.ErrorQueue"/>. Число попыток и кулдаун остаются значениями по умолчанию
    /// Rebus; политика повторов внешних вызовов задаётся каналом (ADR §8.6, §10.2).
    /// </summary>
    public static void ConfigureRetryStrategy(OptionsConfigurer options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.RetryStrategy(errorQueueName: QueueNames.ErrorQueue);
        options.EnableDiagnosticSources();
        options.EnableSynchronousRequestReply();
    }
}
