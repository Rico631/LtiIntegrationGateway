namespace LtiIntegrationGateway.Infrastructure.Persistence.Entities;

/// <summary>Маршрут продукта: какие каналы и адреса очередей ему разрешены.</summary>
public sealed class ProductRoute
{
    public Guid Id { get; set; }

    /// <summary>Идентификатор продукта, формат <c>^[a-z][a-z0-9-]{1,31}$</c>.</summary>
    public required string ProductId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public List<ProductRouteChannel> AllowedChannels { get; set; } = [];

    public List<ProductRouteQueueAddress> AllowedQueueAddresses { get; set; } = [];
}
