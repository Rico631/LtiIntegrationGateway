using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Contracts;
using LtiIntegrationGateway.Infrastructure.Routing;

namespace LtiIntegrationGateway.Features.Contracts;

/// <summary>
/// Действующие настройки операции контракта: включённость и очередь получателя операции
/// переопределяют настройки версии; квота частоты задаётся только версией и общими настройками
/// приложения. Авторизация операцией не переопределяется и всегда берётся из версии (ADR §7.3, §13).
/// </summary>
public sealed record ContractOperationSettings(
    bool Enabled,
    string? RecipientQueue,
    int RateLimitPermit,
    int RateLimitWindowSeconds)
{
    /// <summary>
    /// Действующая очередь получателя без значения приложения: переопределение операции, иначе
    /// очередь версии контракта. Пустая строка трактуется как отсутствие значения.
    /// </summary>
    public static string? ResolveRecipientQueue(ContractOperation operation, ContractVersionSettings version)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(version);

        return NullIfEmpty(operation.RecipientQueue) ?? NullIfEmpty(version.RecipientQueue);
    }

    /// <summary>
    /// Вычисляет действующие настройки операции один раз при построении набора маршрутов: значения
    /// операции, затем версии, затем общих настроек приложения.
    /// </summary>
    public static ContractOperationSettings Resolve(
        ContractOperation operation,
        ContractVersionSettings version,
        InboundRateLimitOptions applicationDefaults)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(applicationDefaults);

        return new ContractOperationSettings(
            operation.Enabled,
            ResolveRecipientQueue(operation, version),
            version.RateLimitPermit ?? applicationDefaults.PermitLimit,
            version.RateLimitWindowSeconds ?? applicationDefaults.WindowSeconds);
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
