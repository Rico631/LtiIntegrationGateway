using LtiIntegrationGateway.Infrastructure.Management;
using LtiIntegrationGateway.Infrastructure.OpenApi;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities;
using LtiIntegrationGateway.Infrastructure.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LtiIntegrationGateway.Features.Contracts;

/// <summary>Активация версии контракта: регистрация маршрутов и снятие предыдущей версии.</summary>
public sealed record ActivateContractVersionCommand(string ChannelName, int Version);

public sealed record ActivateContractVersionResult(int? Version, IReadOnlyList<string> Problems);

public sealed class ActivateContractVersionHandler(
    GatewayDbContext db,
    InboundRouteCatalog catalog,
    InboundSchemaCache schemaCache,
    IOptions<InboundRateLimitOptions> rateLimitOptions,
    ManagementAuditWriter audit)
{
    public async Task<ActivateContractVersionResult> HandleAsync(
        ActivateContractVersionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var target = $"{command.ChannelName}:v{command.Version}";

        var version = await db.ContractVersions
            .Include(entity => entity.Channel)
            .Include(entity => entity.Operations)
            .SingleOrDefaultAsync(
                entity => entity.Channel!.Name == command.ChannelName && entity.Version == command.Version,
                cancellationToken);

        if (version is null)
        {
            await audit.RecordAsync("ActivateContract", target, false, "version: версия контракта не найдена", cancellationToken);

            return new ActivateContractVersionResult(null, ["version: версия контракта не найдена"]);
        }

        if (version.Status == ContractVersionStatus.Draft)
        {
            await audit.RecordAsync("ActivateContract", target, false, "version: версия не опубликована", cancellationToken);

            return new ActivateContractVersionResult(version.Version, ["version: версия не опубликована"]);
        }

        var channelName = version.Channel!.Name;

        var previous = await db.ContractVersions
            .Include(entity => entity.Operations)
            .SingleOrDefaultAsync(
                entity => entity.ChannelId == version.ChannelId &&
                          entity.Status == ContractVersionStatus.Active &&
                          entity.Id != version.Id,
                cancellationToken);

        // Маршруты предыдущей версии того же канала снимаются заранее: иначе новая версия
        // конфликтовала бы сама с собой, а маршруты другого канала остаются нетронутыми.
        if (previous is not null)
        {
            catalog.Deactivate(ContractRouteSets.RevisionKey(previous.Id));
        }

        var candidate = ContractRouteSets.Build(
            version.Id,
            channelName,
            version.Operations,
            ContractVersionSettings.From(version),
            rateLimitOptions.Value);

        var activation = catalog.Activate(candidate);

        if (!activation.Succeeded)
        {
            Restore(catalog, previous, channelName);

            await audit.RecordAsync(
                "ActivateContract",
                target,
                false,
                string.Join("; ", activation.Problems),
                cancellationToken);

            return new ActivateContractVersionResult(version.Version, activation.Problems);
        }

        try
        {
            if (previous is not null)
            {
                previous.Status = ContractVersionStatus.Superseded;
            }

            version.Status = ContractVersionStatus.Active;
            version.ActivatedAt = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            catalog.Deactivate(ContractRouteSets.RevisionKey(version.Id));
            Restore(catalog, previous, channelName);

            throw;
        }

        // Схемы компилируются один раз на ревизию: при деактивации предыдущей версии они удаляются,
        // при активации новой — собираются (ADR §14).
        await schemaCache.WarmUpAsync(version.Id, version.Operations, cancellationToken);

        if (previous is not null)
        {
            schemaCache.Remove(previous.Id);
        }

        await audit.RecordAsync("ActivateContract", target, true, null, cancellationToken);

        return new ActivateContractVersionResult(version.Version, []);
    }

    private void Restore(InboundRouteCatalog catalog, ContractVersion? previous, string channelName)
    {
        if (previous is not null)
        {
            catalog.Activate(ContractRouteSets.Build(
                previous.Id,
                channelName,
                previous.Operations,
                ContractVersionSettings.From(previous),
                rateLimitOptions.Value));
        }
    }


}
