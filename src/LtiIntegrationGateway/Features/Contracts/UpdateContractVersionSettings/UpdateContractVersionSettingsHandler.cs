using LtiIntegrationGateway.Infrastructure.Management;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Contracts;
using Microsoft.EntityFrameworkCore;

namespace LtiIntegrationGateway.Features.Contracts.UpdateContractVersionSettings;

/// <summary>Изменение настроек входящих вызовов версии контракта; только для черновика.</summary>
public sealed record UpdateContractVersionSettingsCommand(
    string ChannelName,
    int Version,
    ContractVersionSettings Settings);

public sealed record UpdateContractVersionSettingsResult(int? Version, IReadOnlyList<string> Problems);

public sealed class UpdateContractVersionSettingsHandler(GatewayDbContext db, ManagementAuditWriter audit)
{
    public async Task<UpdateContractVersionSettingsResult> HandleAsync(
        UpdateContractVersionSettingsCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var target = $"{command.ChannelName}:v{command.Version}";

        var version = await db.ContractVersions
            .Include(entity => entity.InboundChannel)
            .SingleOrDefaultAsync(
                entity => entity.InboundChannel!.Name == command.ChannelName && entity.Version == command.Version,
                cancellationToken);

        if (version is null)
        {
            await audit.RecordAsync("UpdateContractSettings", target, false, "version: версия не найдена", cancellationToken);

            return new UpdateContractVersionSettingsResult(null, ["version: версия контракта не найдена"]);
        }

        if (version.Status != ContractVersionStatus.Draft)
        {
            await audit.RecordAsync("UpdateContractSettings", target, false, "version: опубликованная версия неизменяема", cancellationToken);

            return new UpdateContractVersionSettingsResult(version.Version, ["version: опубликованная версия неизменяема"]);
        }

        var problems = Validate(command.Settings);

        if (problems.Count > 0)
        {
            await audit.RecordAsync("UpdateContractSettings", target, false, string.Join("; ", problems), cancellationToken);

            return new UpdateContractVersionSettingsResult(version.Version, problems);
        }

        var settings = command.Settings;

        version.RecipientQueue = NullIfEmpty(settings.RecipientQueue);
        version.AuthType = settings.AuthType;
        version.ConfigKey = NullIfEmpty(settings.ConfigKey);
        version.HeaderName = NullIfEmpty(settings.HeaderName);
        version.UserName = NullIfEmpty(settings.UserName);
        version.RateLimitPermit = settings.RateLimitPermit;
        version.RateLimitWindowSeconds = settings.RateLimitWindowSeconds;

        await db.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync("UpdateContractSettings", target, true, null, cancellationToken);

        return new UpdateContractVersionSettingsResult(version.Version, []);
    }

    private static IReadOnlyList<string> Validate(ContractVersionSettings settings)
    {
        var problems = new List<string>();

        if (!Enum.IsDefined(settings.AuthType) || settings.AuthType == ChannelAuthType.Bearer)
        {
            problems.Add("auth_type: тип авторизации не поддерживается");
        }

        var requiresSecret = settings.AuthType is ChannelAuthType.ApiKey
            or ChannelAuthType.Basic
            or ChannelAuthType.ClientCertificate;

        if (requiresSecret && string.IsNullOrWhiteSpace(settings.ConfigKey))
        {
            problems.Add("config_key: для выбранного типа авторизации ключ конфигурации обязателен");
        }

        if (settings.AuthType == ChannelAuthType.Basic && string.IsNullOrWhiteSpace(settings.UserName))
        {
            problems.Add("user_name: для типа авторизации basic требуется имя пользователя");
        }

        if (settings.RateLimitPermit is not null || settings.RateLimitWindowSeconds is not null)
        {
            if (settings.RateLimitPermit is null or <= 0 || settings.RateLimitWindowSeconds is null or <= 0)
            {
                problems.Add("rate_limit: квота и окно задаются вместе и должны быть положительными");
            }
        }

        return problems;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
