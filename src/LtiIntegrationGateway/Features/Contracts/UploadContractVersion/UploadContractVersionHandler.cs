using LtiIntegrationGateway.Infrastructure.Management;
using LtiIntegrationGateway.Infrastructure.OpenApi;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Contracts;
using Microsoft.EntityFrameworkCore;

namespace LtiIntegrationGateway.Features.Contracts.UploadContractVersion;

/// <summary>Загрузка OpenAPI-документа: создание черновика версии контракта канала.</summary>
public sealed record UploadContractVersionCommand(string ChannelName, string Document);

public sealed record UploadContractVersionResult(
    int? Version,
    string? SourceFormat,
    IReadOnlyList<string> Problems,
    IReadOnlyList<string> Warnings);

public sealed class UploadContractVersionHandler(
    GatewayDbContext db,
    IOpenApiDocumentReader reader,
    ManagementAuditWriter audit)
{
    public async Task<UploadContractVersionResult> HandleAsync(
        UploadContractVersionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var channel = await db.Channels
            .SingleOrDefaultAsync(entity => entity.Name == command.ChannelName, cancellationToken);

        if (channel is null)
        {
            await audit.RecordAsync("UploadContract", command.ChannelName, false, "channel: канал не найден", cancellationToken);

            return new UploadContractVersionResult(null, null, ["channel: канал не найден"], []);
        }

        var read = await reader.ReadAsync(command.Document, cancellationToken);

        if (!read.Succeeded)
        {
            await audit.RecordAsync(
                "UploadContract",
                command.ChannelName,
                false,
                string.Join("; ", read.Problems),
                cancellationToken);

            return new UploadContractVersionResult(null, null, read.Problems, read.Warnings);
        }

        var nextVersion = (await db.ContractVersions
            .Where(version => version.ChannelId == channel.Id)
            .MaxAsync(version => (int?)version.Version, cancellationToken) ?? 0) + 1;

        db.ContractVersions.Add(new ContractVersion
        {
            Id = Guid.NewGuid(),
            ChannelId = channel.Id,
            Version = nextVersion,
            SourceFormat = read.Format == OpenApiSourceFormat.Swagger20
                ? ContractSourceFormat.Swagger20
                : ContractSourceFormat.OpenApi30,
            Document = command.Document,
            Status = ContractVersionStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(cancellationToken);

        var target = $"{command.ChannelName}:v{nextVersion}";

        await audit.RecordAsync(
            "UploadContract",
            target,
            true,
            read.Format == OpenApiSourceFormat.Swagger20 ? "swagger_2_0_converted" : "openapi_3_0",
            cancellationToken);

        return new UploadContractVersionResult(nextVersion, read.Format.ToString(), [], read.Warnings);
    }

}
