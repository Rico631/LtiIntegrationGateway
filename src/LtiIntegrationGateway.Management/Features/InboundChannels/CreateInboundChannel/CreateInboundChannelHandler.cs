using LtiIntegrationGateway.Features.Channels;
using LtiIntegrationGateway.Infrastructure.OpenApi;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Contracts;
using Microsoft.EntityFrameworkCore;

namespace LtiIntegrationGateway.Features.InboundChannels.CreateInboundChannel;

/// <summary>Создание входящего канала и черновика его первого OpenAPI-контракта.</summary>
public sealed record CreateInboundChannelCommand(
    string Name,
    string Document,
    InboundResponseMode? ResponseMode = null,
    int? InboundResponseTimeoutMs = null,
    string IntegrationId = IntegrationDefaults.Default);

/// <summary>Результат создания входящего канала.</summary>
public sealed record CreateInboundChannelResult(
    Guid? ChannelId,
    int? ContractVersion,
    IReadOnlyList<string> Problems,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Создание входящего канала: имя, обязательный OpenAPI-документ и настройки режима ответа.
/// Канал и черновик версии 1 сохраняются одной транзакцией.
/// </summary>
public sealed class CreateInboundChannelHandler(
    GatewayDbContext db,
    IOpenApiDocumentReader reader)
{
    public async Task<CreateInboundChannelResult> HandleAsync(
        CreateInboundChannelCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var problems = new List<string>();
        var mode = command.ResponseMode ?? InboundResponseMode.Async;
        var timeout = command.InboundResponseTimeoutMs ?? 30_000;

        if (string.IsNullOrWhiteSpace(command.Name))
        {
            problems.Add("name: обязательное поле не заполнено");
        }
        else if (command.Name.Length > 64)
        {
            problems.Add("name: длина имени превышает 64 символа");
        }
        else if (await db.InboundChannels.AnyAsync(
                     channel => channel.IntegrationId == command.IntegrationId && channel.Name == command.Name,
                     cancellationToken))
        {
            problems.Add("name: входящий канал с таким именем уже существует");
        }

        if (timeout <= 0)
        {
            problems.Add("inbound_response_timeout_ms: значение должно быть положительным");
        }

        if (problems.Count > 0)
        {
            return new CreateInboundChannelResult(null, null, problems, []);
        }

        var read = await reader.ReadAsync(command.Document, cancellationToken);

        if (!read.Succeeded)
        {
            return new CreateInboundChannelResult(null, null, read.Problems, read.Warnings);
        }

        var channel = new InboundChannel
        {
            Id = Guid.NewGuid(),
            IntegrationId = command.IntegrationId,
            Name = command.Name,
            ResponseMode = mode,
            InboundResponseTimeoutMs = timeout,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var version = new ContractVersion
        {
            Id = Guid.NewGuid(),
            InboundChannelId = channel.Id,
            Version = 1,
            SourceFormat = read.Format == OpenApiSourceFormat.Swagger20
                ? ContractSourceFormat.Swagger20
                : ContractSourceFormat.OpenApi30,
            Document = command.Document,
            Status = ContractVersionStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        channel.ContractVersions.Add(version);
        db.InboundChannels.Add(channel);
        ChannelAuditWriter.Add(
            db,
            channel.Id,
            revisionNumber: null,
            "create-inbound-channel",
            direction: ChannelDirection.Inbound);

        await IntegrationRegistry.BumpAsync(db, command.IntegrationId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateInboundChannelResult(channel.Id, version.Version, [], read.Warnings);
    }
}
