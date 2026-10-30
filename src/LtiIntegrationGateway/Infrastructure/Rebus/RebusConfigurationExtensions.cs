using Rebus.Config;
using Rebus.Transport.InMem;

namespace LtiIntegrationGateway.Infrastructure.Rebus;

/// <summary>
/// Подключение шины шлюза: транспорт, входная очередь и сериализатор.
/// </summary>
public static class RebusConfigurationExtensions
{
    /// <summary>Входная очередь шлюза: сюда продукты отправляют <c>OutboundRequest</c>.</summary>
    public const string OutboundQueue = "lti.gateway.outbound";

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
                .Transport(transport => transport.UseInMemoryTransport(new InMemNetwork(), OutboundQueue))
                .Serialization(serialization => serialization.Register(_ => serializer)));

        return services;
    }
}
