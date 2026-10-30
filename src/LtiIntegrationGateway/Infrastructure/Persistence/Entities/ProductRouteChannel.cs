namespace LtiIntegrationGateway.Infrastructure.Persistence.Entities;

/// <summary>Канал, разрешённый продукту.</summary>
public sealed class ProductRouteChannel
{
    public Guid Id { get; set; }

    public Guid ProductRouteId { get; set; }

    public ProductRoute? ProductRoute { get; set; }

    public required string ChannelName { get; set; }
}
