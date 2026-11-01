using LtiIntegrationGateway.Features.Configuration;
using LtiIntegrationGateway.Messaging;
using Microsoft.Extensions.Options;
using Rebus.Config;
using Rebus.Retry.Simple;

namespace LtiIntegrationGateway.Infrastructure.Rebus;

/// <summary>
/// Подключение шины шлюза: транспорт, входная очередь интеграции, error-queue и сериализатор.
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
            (configure, provider) =>
            {
                var connectionString = provider.GetRequiredService<IConfiguration>().GetConnectionString("Gateway")
                    ?? throw new InvalidOperationException(
                        "Строка подключения к хранилищу не настроена. " +
                        "Задайте ключ конфигурации 'ConnectionStrings:Gateway'.");

                // Экземпляр обслуживает одну интеграцию: её имя задаёт входную очередь и error-queue.
                var options = provider.GetRequiredService<IOptions<FlowOptions>>().Value;

                options.Validate();

                return configure
                    .Transport(transport => transport.UsePostgreSql(
                        connectionString,
                        tableName: QueueNames.MessageTable,
                        inputQueueName: QueueNames.OutboundQueue(options.Integration)))
                    .Options(configurer => ConfigureRetryStrategy(configurer, options.Integration))
                    .Serialization(serialization => serialization.Register(_ => serializer));
            });

        return services;
    }

    /// <summary>
    /// Настройка повторов шины: сообщение, не обработанное после всех попыток, попадает в
    /// error-queue своей интеграции <see cref="QueueNames.ErrorQueue(string)"/>. Число попыток и
    /// кулдаун остаются значениями по умолчанию Rebus; политика повторов внешних вызовов задаётся
    /// каналом.
    /// </summary>
    public static void ConfigureRetryStrategy(OptionsConfigurer options, string integrationId)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.RetryStrategy(errorQueueName: QueueNames.ErrorQueue(integrationId));
        options.EnableDiagnosticSources();
    }
}

