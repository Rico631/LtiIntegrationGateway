namespace LtiIntegrationGateway.Infrastructure.Persistence.Entities.ProductRoutes;

/// <summary>Маршрут продукта: какие каналы и адреса очередей ему разрешены.</summary>
public sealed class ProductRoute : IEntity
{
    /// <summary>Уникальный идентификатор записи.</summary>
    public Guid Id { get; set; }

    /// <summary>Идентификатор продукта, формат <c>^[a-z][a-z0-9-]{1,31}$</c>.</summary>
    public required string ProductId { get; set; }

    /// <summary>Момент создания записи в UTC.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Каналы, разрешённые продукту.</summary>
    public List<ProductRouteChannel> AllowedChannels { get; set; } = [];

    /// <summary>Адреса очередей, разрешённые продукту.</summary>
    public List<ProductRouteQueueAddress> AllowedQueueAddresses { get; set; } = [];
}
