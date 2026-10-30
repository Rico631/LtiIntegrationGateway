namespace LtiIntegrationGateway.Infrastructure.Persistence.Entities;

/// <summary>Адрес очереди, разрешённый продукту: отправитель запроса и получатель результата.</summary>
public sealed class ProductRouteQueueAddress
{
    public Guid Id { get; set; }

    public Guid ProductRouteId { get; set; }

    public ProductRoute? ProductRoute { get; set; }

    public required string Address { get; set; }
}
