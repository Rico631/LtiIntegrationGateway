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
public sealed partial class ManagementApiService : ManagementService.ManagementServiceBase
{
    private readonly GatewayDbContext _db;
    private readonly CreateChannelHandler _createChannel;
    private readonly UpdateChannelDraftHandler _updateChannelDraft;
    private readonly PublishChannelRevisionHandler _publishChannelRevision;
    private readonly ActivateChannelRevisionHandler _activateChannelRevision;
    private readonly RollbackChannelRevisionHandler _rollbackChannelRevision;
    private readonly GetChannelHandler _getChannel;
    private readonly RegisterProductRouteHandler _registerProductRoute;
    private readonly GetProductRouteHandler _getProductRoute;
    private readonly UploadContractVersionHandler _uploadContractVersion;
    private readonly PublishContractVersionHandler _publishContractVersion;
    private readonly ActivateContractVersionHandler _activateContractVersion;
    private readonly ListContractOperationsHandler _listContractOperations;
    private readonly ManagementAuditWriter _audit;

    public ManagementApiService(
        GatewayDbContext db,
        CreateChannelHandler createChannel,
        UpdateChannelDraftHandler updateChannelDraft,
        PublishChannelRevisionHandler publishChannelRevision,
        ActivateChannelRevisionHandler activateChannelRevision,
        RollbackChannelRevisionHandler rollbackChannelRevision,
        GetChannelHandler getChannel,
        RegisterProductRouteHandler registerProductRoute,
        GetProductRouteHandler getProductRoute,
        UploadContractVersionHandler uploadContractVersion,
        PublishContractVersionHandler publishContractVersion,
        ActivateContractVersionHandler activateContractVersion,
        ListContractOperationsHandler listContractOperations,
        ManagementAuditWriter audit)
    {
        _db = db;
        _createChannel = createChannel;
        _updateChannelDraft = updateChannelDraft;
        _publishChannelRevision = publishChannelRevision;
        _activateChannelRevision = activateChannelRevision;
        _rollbackChannelRevision = rollbackChannelRevision;
        _getChannel = getChannel;
        _registerProductRoute = registerProductRoute;
        _getProductRoute = getProductRoute;
        _uploadContractVersion = uploadContractVersion;
        _publishContractVersion = publishContractVersion;
        _activateContractVersion = activateContractVersion;
        _listContractOperations = listContractOperations;
        _audit = audit;
    }

    public override async Task<ListChannelsResponse> ListChannels(
        ListChannelsRequest request,
        ServerCallContext context)
    {
        var channels = await _db.Channels
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
        var channelId = await RequireChannelIdAsync(request.ChannelName, context.CancellationToken);
        var result = await _getChannel.HandleAsync(new GetChannelQuery(channelId), context.CancellationToken);

        if (result.Channel is null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "channel not found"));
        }

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

    private async Task<Guid> RequireChannelIdAsync(string channelName, CancellationToken cancellationToken)
    {
        var channelId = await _db.Channels
            .Where(channel => channel.Name == channelName)
            .Select(channel => (Guid?)channel.Id)
            .SingleOrDefaultAsync(cancellationToken);

        return channelId ?? throw new RpcException(new Status(StatusCode.NotFound, "channel not found"));
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
        _audit.RecordAsync(
            operation,
            target,
            succeeded,
            problems.Count == 0 ? null : Truncate(string.Join("; ", problems)),
            cancellationToken);

    private static string Truncate(string value) => value.Length <= 2048 ? value : value[..2048];
}
