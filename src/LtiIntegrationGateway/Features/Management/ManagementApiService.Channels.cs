using Grpc.Core;
using LtiIntegrationGateway.Contracts.Management.V1;

namespace LtiIntegrationGateway.Features.Management;

/// <summary>Операции каналов через управляющий API.</summary>
public sealed partial class ManagementApiService
{
    public override async Task<ChannelOperationResponse> CreateChannel(
        CreateChannelRequest request,
        ServerCallContext context)
    {
        var configuration = ChannelMapping.ToConfiguration(request.Settings, out var mappingProblems);

        if (mappingProblems.Count > 0)
        {
            await AuditAsync("CreateChannel", request.Name, false, mappingProblems, context.CancellationToken);

            return ToChannelResponse(false, mappingProblems);
        }

        var result = await _createChannel.HandleAsync(
            new Channels.CreateChannelCommand(request.Name, configuration),
            context.CancellationToken);

        var succeeded = result.Problems.Count == 0;

        await AuditAsync("CreateChannel", request.Name, succeeded, result.Problems, context.CancellationToken);

        return ToChannelResponse(succeeded, result.Problems, result.ChannelId, result.RevisionNumber);
    }

    public override async Task<ChannelOperationResponse> UpdateChannelDraft(
        UpdateChannelDraftRequest request,
        ServerCallContext context)
    {
        var channelId = await RequireChannelIdAsync(request.ChannelName, context.CancellationToken);
        var configuration = ChannelMapping.ToConfiguration(request.Settings, out var mappingProblems);

        if (mappingProblems.Count > 0)
        {
            await AuditAsync("UpdateChannelDraft", request.ChannelName, false, mappingProblems, context.CancellationToken);

            return ToChannelResponse(false, mappingProblems, channelId);
        }

        var result = await _updateChannelDraft.HandleAsync(
            new Channels.UpdateChannelDraftCommand(channelId, Optional(request.Revision), configuration),
            context.CancellationToken);

        var succeeded = result.Problems.Count == 0;

        await AuditAsync("UpdateChannelDraft", request.ChannelName, succeeded, result.Problems, context.CancellationToken);

        return ToChannelResponse(succeeded, result.Problems, channelId, result.RevisionNumber);
    }

    public override async Task<ChannelOperationResponse> PublishChannelRevision(
        PublishChannelRevisionRequest request,
        ServerCallContext context)
    {
        var channelId = await RequireChannelIdAsync(request.ChannelName, context.CancellationToken);

        var result = await _publishChannelRevision.HandleAsync(
            new Channels.PublishChannelRevisionCommand(channelId, Optional(request.Revision)),
            context.CancellationToken);

        var succeeded = result.Problems.Count == 0;

        await AuditAsync("PublishChannelRevision", request.ChannelName, succeeded, result.Problems, context.CancellationToken);

        return ToChannelResponse(succeeded, result.Problems, channelId, result.RevisionNumber);
    }

    public override async Task<ChannelOperationResponse> ActivateChannelRevision(
        ActivateChannelRevisionRequest request,
        ServerCallContext context)
    {
        var channelId = await RequireChannelIdAsync(request.ChannelName, context.CancellationToken);

        var result = await _activateChannelRevision.HandleAsync(
            new Channels.ActivateChannelRevisionCommand(channelId, request.Revision),
            context.CancellationToken);

        var succeeded = result.Problems.Count == 0;

        await AuditAsync("ActivateChannelRevision", request.ChannelName, succeeded, result.Problems, context.CancellationToken);

        return ToChannelResponse(succeeded, result.Problems, channelId, result.RevisionNumber);
    }

    public override async Task<ChannelOperationResponse> RollbackChannelRevision(
        RollbackChannelRevisionRequest request,
        ServerCallContext context)
    {
        var channelId = await RequireChannelIdAsync(request.ChannelName, context.CancellationToken);

        var result = await _rollbackChannelRevision.HandleAsync(
            new Channels.RollbackChannelRevisionCommand(channelId),
            context.CancellationToken);

        var succeeded = result.Problems.Count == 0;

        await AuditAsync("RollbackChannelRevision", request.ChannelName, succeeded, result.Problems, context.CancellationToken);

        return ToChannelResponse(succeeded, result.Problems, channelId, result.RevisionNumber);
    }

    private static int? Optional(int revision) => revision <= 0 ? null : revision;
}
