namespace LtiIntegrationGateway.Infrastructure.Routing;

/// <summary>Канал, которому принадлежит динамический endpoint; используется как ключ квоты частоты.</summary>
public sealed record InboundChannelMetadata(string ChannelName);
