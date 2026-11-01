using LtiIntegrationGateway.Features.Channels;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;
using LtiIntegrationGateway.Infrastructure.Routing;
using Microsoft.EntityFrameworkCore;

namespace LtiIntegrationGateway.Features.InboundChannels.UpdateInboundChannelSettings;

/// <summary>Изменение режима ответа и таймаута входящего канала.</summary>
public sealed record UpdateInboundChannelSettingsCommand(
    string ChannelName,
    InboundResponseMode? ResponseMode = null,
    int? InboundResponseTimeoutMs = null);

/// <summary>Результат изменения настроек входящего канала.</summary>
public sealed record UpdateInboundChannelSettingsResult(
    Guid? ChannelId,
    IReadOnlyList<string> Problems);

/// <summary>
/// Изменение channel-level настроек входящего канала. Уже начатые ожидания используют
/// snapshot режима и таймаута, поэтому изменение не влияет на текущий запрос.
/// </summary>
public sealed class UpdateInboundChannelSettingsHandler(
    GatewayDbContext db,
    InboundRouteCatalog catalog)
{
    public async Task<UpdateInboundChannelSettingsResult> HandleAsync(
        UpdateInboundChannelSettingsCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var channel = await db.InboundChannels
            .SingleOrDefaultAsync(entity => entity.Name == command.ChannelName, cancellationToken);

        if (channel is null)
        {
            return new UpdateInboundChannelSettingsResult(null, ["channel: входящий канал не найден"]);
        }

        if (command.ResponseMode is null && command.InboundResponseTimeoutMs is null)
        {
            return new UpdateInboundChannelSettingsResult(channel.Id, ["settings: не задано ни одного поля"]);
        }

        if (command.InboundResponseTimeoutMs is <= 0)
        {
            return new UpdateInboundChannelSettingsResult(
                channel.Id,
                ["inbound_response_timeout_ms: значение должно быть положительным"]);
        }

        if (command.ResponseMode is { } mode)
        {
            channel.ResponseMode = mode;
        }

        if (command.InboundResponseTimeoutMs is { } timeout)
        {
            channel.InboundResponseTimeoutMs = timeout;
        }

        catalog.UpdateResponseSettings(
            channel.Name,
            channel.ResponseMode,
            channel.InboundResponseTimeoutMs);

        ChannelAuditWriter.Add(
            db,
            channel.Id,
            revisionNumber: null,
            "update-inbound-channel-settings",
            direction: ChannelDirection.Inbound);

        await db.SaveChangesAsync(cancellationToken);

        return new UpdateInboundChannelSettingsResult(channel.Id, []);
    }
}
