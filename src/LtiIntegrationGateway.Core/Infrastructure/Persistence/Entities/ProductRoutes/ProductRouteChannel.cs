namespace LtiIntegrationGateway.Infrastructure.Persistence.Entities.ProductRoutes;

/// <summary>Канал, разрешённый продукту.</summary>
public sealed class ProductRouteChannel : IEntity
{
    /// <summary>Уникальный идентификатор записи.</summary>
    public Guid Id { get; set; }

    /// <summary>Идентификатор маршрута продукта.</summary>
    public Guid ProductRouteId { get; set; }

    /// <summary>Навигационное свойство маршрута продукта.</summary>
    public ProductRoute? ProductRoute { get; set; }

    /// <summary>Имя канала.</summary>
    public required string ChannelName { get; set; }
}
