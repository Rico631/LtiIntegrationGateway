using Grpc.Core;
using LtiIntegrationGateway.Contracts.Management.V1;
using LtiIntegrationGateway.Features.Channels;
using LtiIntegrationGateway.Features.Contracts;
using LtiIntegrationGateway.Features.ProductRoutes;
using LtiIntegrationGateway.Infrastructure.Management;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace LtiIntegrationGateway.Features.Management;

/// <summary>
/// Управляющий API: тонкий адаптер над срезами. Доменные правила остаются в срезах, здесь —
/// отображение proto-сообщений, коды gRPC и аудит управляющих операций.
/// </summary>
public sealed partial class ManagementApiService(
    GatewayDbContext db,
    CreateChannelHandler createChannel,
    UpdateChannelDraftHandler updateChannelDraft,
    PublishChannelRevisionHandler publishChannelRevision,
    ActivateChannelRevisionHandler activateChannelRevision,
    RollbackChannelRevisionHandler rollbackChannelRevision,
    GetChannelHandler getChannel,
    GetChannelHealthHandler getChannelHealth,
    RegisterProductRouteHandler registerProductRoute,
    GetProductRouteHandler getProductRoute,
    UploadContractVersionHandler uploadContractVersion,
    PublishContractVersionHandler publishContractVersion,
    ActivateContractVersionHandler activateContractVersion,
    ListContractOperationsHandler listContractOperations,
    GetContractVersionHandler getContractVersion,
    UpdateContractVersionSettingsHandler updateContractVersionSettings,
    UpdateContractOperationSettingsHandler updateContractOperationSettings,
    ManagementAuditWriter audit) : ManagementService.ManagementServiceBase
{

    public override async Task<ListChannelsResponse> ListChannels(
        ListChannelsRequest request,
        ServerCallContext context)
    {
        var channels = await db.Channels
            .AsNoTracking()
            .Include(channel => channel.Revisions).ThenInclude(revision => revision.AllowedMethods)
            .Include(channel => channel.Revisions).ThenInclude(revision => revision.AllowedPaths)
            .OrderBy(channel => channel.Name)
            .ToListAsync(context.CancellationToken);

        var response = new ListChannelsResponse();

        foreach (var channel in channels)
        {
            response.Channels.Add(ChannelMapping.ToInfo(ToDetails(channel)));
        }

        await AuditAsync("ListChannels", "channels", true, [], context.CancellationToken);

        return response;
    }

    public override async Task<GetChannelResponse> GetChannel(
        GetChannelRequest request,
        ServerCallContext context)
    {
        var channelId = await RequireChannelIdAsync("GetChannel", request.ChannelName, context.CancellationToken);
        var result = await getChannel.HandleAsync(new GetChannelQuery(channelId), context.CancellationToken);

        if (result.Channel is null)
        {
            await AuditAsync("GetChannel", request.ChannelName, false, result.Problems, context.CancellationToken);

            throw new RpcException(new Status(StatusCode.NotFound, "channel not found"));
        }

        await AuditAsync("GetChannel", request.ChannelName, true, [], context.CancellationToken);

        return new GetChannelResponse { Channel = ChannelMapping.ToInfo(result.Channel) };
    }

    private static ChannelDetails ToDetails(Channel channel)
    {
        var active = channel.Revisions.SingleOrDefault(revision => revision.Status == ChannelRevisionStatus.Active);

        return new ChannelDetails(
            channel.Id,
            channel.Name,
            active is null ? null : new ChannelRevisionDetails(active.Number, ChannelRevisionWriter.Read(active)),
            channel.Revisions
                .OrderBy(revision => revision.Number)
                .Select(revision => new ChannelRevisionSummary(revision.Number, revision.Status))
                .ToArray());
    }

    private async Task<Guid> RequireChannelIdAsync(
        string operation,
        string channelName,
        CancellationToken cancellationToken)
    {
        var channelId = await db.Channels
            .Where(channel => channel.Name == channelName)
            .Select(channel => (Guid?)channel.Id)
            .SingleOrDefaultAsync(cancellationToken);

        if (channelId is null)
        {
            await AuditAsync(operation, channelName, false, ["channel: канал не найден"], cancellationToken);

            throw new RpcException(new Status(StatusCode.NotFound, "channel not found"));
        }

        return channelId.Value;
    }

    private static ChannelOperationResponse ToChannelResponse(
        bool succeeded,
        IReadOnlyList<string> problems,
        Guid? channelId = null,
        int? revision = null)
    {
        var response = new ChannelOperationResponse
        {
            Succeeded = succeeded,
            ChannelId = channelId?.ToString() ?? string.Empty,
            Revision = revision ?? 0,
        };

        response.Problems.AddRange(problems.Select(ToProblem));

        return response;
    }

    private static OperationResponse ToOperationResponse(bool succeeded, IReadOnlyList<string> problems)
    {
        var response = new OperationResponse { Succeeded = succeeded };
        response.Problems.AddRange(problems.Select(ToProblem));

        return response;
    }

    private static OperationProblem ToProblem(string problem)
    {
        var separator = problem.IndexOf(':');

        return separator < 0
            ? new OperationProblem { Code = "problem", Message = problem }
            : new OperationProblem
            {
                Code = problem[..separator].Trim(),
                Message = problem[(separator + 1)..].Trim(),
            };
    }

    private Task AuditAsync(
        string operation,
        string target,
        bool succeeded,
        IReadOnlyList<string> problems,
        CancellationToken cancellationToken) =>
        audit.RecordAsync(
            operation,
            target,
            succeeded,
            problems.Count == 0 ? null : string.Join("; ", problems),
            cancellationToken);

}
