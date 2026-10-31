namespace LtiIntegrationGateway.Infrastructure.Persistence.Entities.ProductRoutes;

/// <summary>Адрес очереди, разрешённый продукту: отправитель запроса и получатель результата.</summary>
public sealed class ProductRouteQueueAddress : IEntity
{
    /// <summary>Уникальный идентификатор записи.</summary>
    public Guid Id { get; set; }

    /// <summary>Идентификатор маршрута продукта.</summary>
    public Guid ProductRouteId { get; set; }

    /// <summary>Навигационное свойство маршрута продукта.</summary>
    public ProductRoute? ProductRoute { get; set; }

    /// <summary>Адрес очереди.</summary>
    public required string Address { get; set; }
}
