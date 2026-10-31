using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;

namespace LtiIntegrationGateway.Infrastructure.Routing;

/// <summary>Канал, которому принадлежит динамический endpoint; используется как ключ квоты частоты.</summary>
public sealed record InboundChannelMetadata(string ChannelName);

/// <summary>
/// Настройки активного маршрута: ревизия контракта, операция, очередь получателя, авторизация
/// и квота частоты. Передаются в метаданные endpoint'а, чтобы приём не обращался к базе.
/// </summary>
public sealed record InboundRouteMetadata(
    string ChannelName,
    Guid? ContractVersionId,
    string? OperationId,
    string? RecipientQueue,
    ChannelAuthType AuthType,
    string? ConfigKey,
    string? HeaderName,
    string? UserName,
    int? RateLimitPermit,
    int? RateLimitWindowSeconds);

