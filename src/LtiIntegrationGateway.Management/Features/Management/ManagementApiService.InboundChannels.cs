using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using LtiIntegrationGateway.Contracts.Management.V1;
using LtiIntegrationGateway.Features.InboundChannels.CreateInboundChannel;
using LtiIntegrationGateway.Features.InboundChannels.UpdateInboundChannelSettings;
using Microsoft.EntityFrameworkCore;
using EntityResponseMode = LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels.InboundResponseMode;
using ProtoResponseMode = LtiIntegrationGateway.Contracts.Management.V1.InboundResponseMode;

namespace LtiIntegrationGateway.Features.Management;

/// <summary>Операции входящих каналов через управляющий API.</summary>
public sealed partial class ManagementApiService
{
    public override async Task<ListInboundChannelsResponse> ListInboundChannels(
        ListInboundChannelsRequest request,
        ServerCallContext context)
    {
        var channels = await db.InboundChannels
            .AsNoTracking()
            .Where(entity => entity.IntegrationId == IntegrationOf(request.Integration))
            .Include(entity => entity.ContractVersions)
            .OrderBy(entity => entity.Name)
            .ToListAsync(context.CancellationToken);

        var response = new ListInboundChannelsResponse();

        foreach (var channel in channels)
        {
            var active = channel.ContractVersions.SingleOrDefault(
                version => version.Status == Infrastructure.Persistence.Entities.Contracts.ContractVersionStatus.Active);

            var info = new InboundChannelInfo
            {
                ChannelId = channel.Id.ToString(),
                Name = channel.Name,
                Settings = new InboundChannelSettings
                {
                    ResponseMode = ToProtoMode(channel.ResponseMode),
                    InboundResponseTimeoutMs = channel.InboundResponseTimeoutMs,
                },
                ActiveContractVersion = active?.Version ?? 0,
            };

            info.ContractVersions.AddRange(channel.ContractVersions
                .OrderBy(version => version.Version)
                .Select(version => new InboundContractVersionInfo
                {
                    Version = version.Version,
                    Status = version.Status.ToString(),
                }));

            response.Channels.Add(info);
        }

        await AuditAsync("ListInboundChannels", "inbound-channels", true, [], context.CancellationToken);

        return response;
    }

    public override async Task<InboundChannelOperationResponse> CreateInboundChannel(
        CreateInboundChannelRequest request,
        ServerCallContext context)
    {
        var mode = request.HasResponseMode
            ? ToEntityMode(request.ResponseMode)
            : (EntityResponseMode?)null;
        var timeout = request.HasInboundResponseTimeoutMs ? request.InboundResponseTimeoutMs : (int?)null;

        if (request.HasInboundResponseTimeoutMs && request.InboundResponseTimeoutMs <= 0)
        {
            var problem = new InboundChannelOperationResponse { Succeeded = false };
            problem.Problems.Add(ToProblem("inbound_response_timeout_ms: значение должно быть положительным"));

            await AuditAsync(
                "CreateInboundChannel",
                request.Name,
                false,
                ["inbound_response_timeout_ms: значение должно быть положительным"],
                context.CancellationToken);

            return problem;
        }

        var result = await createInboundChannel.HandleAsync(
            new CreateInboundChannelCommand(
                request.Name,
                request.Document,
                mode,
                timeout,
                IntegrationOf(request.Integration)),
            context.CancellationToken);

        var succeeded = result.Problems.Count == 0;

        await AuditAsync("CreateInboundChannel", request.Name, succeeded, result.Problems, context.CancellationToken);

        var response = new InboundChannelOperationResponse
        {
            Succeeded = succeeded,
            ChannelId = result.ChannelId?.ToString() ?? string.Empty,
            ContractVersion = result.ContractVersion ?? 0,
        };

        response.Problems.AddRange(result.Problems.Select(ToProblem));
        response.Problems.AddRange(result.Warnings.Select(ToWarning));

        return response;
    }

    public override async Task<GetInboundChannelResponse> GetInboundChannel(
        GetInboundChannelRequest request,
        ServerCallContext context)
    {
        var channel = await db.InboundChannels
            .AsNoTracking()
            .Include(entity => entity.ContractVersions)
            .SingleOrDefaultAsync(
                entity => entity.IntegrationId == IntegrationOf(request.Integration) &&
                    entity.Name == request.ChannelName,
                context.CancellationToken);

        if (channel is null)
        {
            await AuditAsync(
                "GetInboundChannel",
                request.ChannelName,
                false,
                ["channel: входящий канал не найден"],
                context.CancellationToken);

            throw new RpcException(new Status(StatusCode.NotFound, "inbound channel not found"));
        }

        var active = channel.ContractVersions.SingleOrDefault(
            version => version.Status == Infrastructure.Persistence.Entities.Contracts.ContractVersionStatus.Active);

        var info = new InboundChannelInfo
        {
            ChannelId = channel.Id.ToString(),
            Name = channel.Name,
            Settings = new InboundChannelSettings
            {
                ResponseMode = ToProtoMode(channel.ResponseMode),
                InboundResponseTimeoutMs = channel.InboundResponseTimeoutMs,
            },
            ActiveContractVersion = active?.Version ?? 0,
        };

        info.ContractVersions.AddRange(channel.ContractVersions
            .OrderBy(version => version.Version)
            .Select(version => new InboundContractVersionInfo
            {
                Version = version.Version,
                Status = version.Status.ToString(),
            }));

        await AuditAsync("GetInboundChannel", request.ChannelName, true, [], context.CancellationToken);

        return new GetInboundChannelResponse { Channel = info };
    }

    public override async Task<GetInboundChannelHealthResponse> GetInboundChannelHealth(
        GetInboundChannelHealthRequest request,
        ServerCallContext context)
    {
        var channel = await db.InboundChannels
            .AsNoTracking()
            .Include(entity => entity.ContractVersions)
            .SingleOrDefaultAsync(
                entity => entity.IntegrationId == IntegrationOf(request.Integration) &&
                    entity.Name == request.ChannelName,
                context.CancellationToken);

        if (channel is null)
        {
            await AuditAsync(
                "GetInboundChannelHealth",
                request.ChannelName,
                false,
                ["channel: входящий канал не найден"],
                context.CancellationToken);

            throw new RpcException(new Status(StatusCode.NotFound, "inbound channel not found"));
        }

        var active = channel.ContractVersions.SingleOrDefault(
            version => version.Status == Infrastructure.Persistence.Entities.Contracts.ContractVersionStatus.Active);

        var errors = await db.InboundMessageLogs
            .AsNoTracking()
            .Where(log => log.ChannelName == channel.Name && log.ErrorCode != null)
            .OrderByDescending(log => log.ReceivedAt)
            .Take(5)
            .Select(log => new { log.ErrorCode, log.ReceivedAt, log.ResponseStatusCode })
            .ToListAsync(context.CancellationToken);

        var response = new GetInboundChannelHealthResponse
        {
            ChannelName = channel.Name,
            State = active is null ? "NO_ACTIVE_CONTRACT" : "ACTIVE",
            ActiveContractVersion = active?.Version ?? 0,
            CheckedAt = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
        };

        response.Errors.AddRange(errors.Select(error => new ChannelHealthError
        {
            OccurredAt = Timestamp.FromDateTimeOffset(error.ReceivedAt),
            Direction = "inbound",
            ErrorCode = error.ErrorCode!,
            HttpStatus = error.ResponseStatusCode ?? 0,
            Outcome = string.Empty,
        }));

        await AuditAsync("GetInboundChannelHealth", request.ChannelName, true, [], context.CancellationToken);

        return response;
    }

    public override async Task<InboundChannelOperationResponse> UpdateInboundChannelSettings(
        UpdateInboundChannelSettingsRequest request,
        ServerCallContext context)
    {
        var result = await updateInboundChannelSettings.HandleAsync(
            new UpdateInboundChannelSettingsCommand(
                request.ChannelName,
                request.HasResponseMode ? ToEntityMode(request.ResponseMode) : null,
                request.HasInboundResponseTimeoutMs ? request.InboundResponseTimeoutMs : null,
                IntegrationOf(request.Integration)),
            context.CancellationToken);

        var succeeded = result.Problems.Count == 0;

        await AuditAsync(
            "UpdateInboundChannelSettings",
            request.ChannelName,
            succeeded,
            result.Problems,
            context.CancellationToken);

        var response = new InboundChannelOperationResponse
        {
            Succeeded = succeeded,
            ChannelId = result.ChannelId?.ToString() ?? string.Empty,
        };

        response.Problems.AddRange(result.Problems.Select(ToProblem));

        return response;
    }

    private static EntityResponseMode ToEntityMode(ProtoResponseMode mode) =>
        mode == ProtoResponseMode.Sync ? EntityResponseMode.Sync : EntityResponseMode.Async;

    private static ProtoResponseMode ToProtoMode(EntityResponseMode mode) =>
        mode == EntityResponseMode.Sync ? ProtoResponseMode.Sync : ProtoResponseMode.Async;
}

