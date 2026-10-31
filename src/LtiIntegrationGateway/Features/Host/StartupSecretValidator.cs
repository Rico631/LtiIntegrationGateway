using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities;
using LtiIntegrationGateway.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace LtiIntegrationGateway.Features.Host;

/// <summary>
/// Проверка секретов активной конфигурации при старте: приложение не должно работать с каналом,
/// для которого авторизация настроена, но значение секрета отсутствует (ADR §18).
/// </summary>
/// <remarks>
/// Проверяются только активные ревизии каналов и активные версии контрактов. Черновики и
/// опубликованные, но не активные версии не блокируют запуск. Сообщения содержат имена ключей
/// конфигурации, но не значения секретов.
/// </remarks>
public sealed class StartupSecretValidator(
    GatewayDbContext db,
    ISecretStore secrets,
    IChannelCertificateStore certificates)
{
    public async Task<IReadOnlyList<string>> FindMissingAsync(CancellationToken cancellationToken = default)
    {
        var missing = new List<string>();

        var channels = await db.ChannelRevisions
            .AsNoTracking()
            .Where(revision => revision.Status == ChannelRevisionStatus.Active &&
                revision.AuthType != ChannelAuthType.None)
            .Select(revision => new
            {
                revision.Channel!.Name,
                revision.AuthType,
                revision.ConfigKey,
            })
            .ToListAsync(cancellationToken);

        foreach (var channel in channels)
        {
            if (!IsConfigured(channel.AuthType, channel.ConfigKey))
            {
                missing.Add($"канал '{channel.Name}': ключ '{KeyText(channel.ConfigKey)}'");
            }
        }

        var contracts = await db.ContractVersions
            .AsNoTracking()
            .Where(version => version.Status == ContractVersionStatus.Active &&
                version.AuthType != ChannelAuthType.None)
            .Select(version => new
            {
                version.Channel!.Name,
                version.Version,
                version.AuthType,
                version.ConfigKey,
            })
            .ToListAsync(cancellationToken);

        foreach (var contract in contracts)
        {
            if (!IsConfigured(contract.AuthType, contract.ConfigKey))
            {
                missing.Add(
                    $"контракт '{contract.Name}' v{contract.Version}: ключ '{KeyText(contract.ConfigKey)}'");
            }
        }

        return missing;
    }

    private bool IsConfigured(ChannelAuthType authType, string? configKey) =>
        authType == ChannelAuthType.ClientCertificate
            ? certificates.GetInfo(configKey).Found
            : secrets.Find(configKey ?? string.Empty) is not null;

    private static string KeyText(string? configKey) =>
        string.IsNullOrWhiteSpace(configKey) ? "<не задан>" : configKey;
}
