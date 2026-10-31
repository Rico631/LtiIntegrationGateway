using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using LtiIntegrationGateway.Contracts.Management.V1;
using LtiIntegrationGateway.Features.Channels;

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

        var result = await createChannel.HandleAsync(
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
        var channelId = await RequireChannelIdAsync("UpdateChannelDraft", request.ChannelName, context.CancellationToken);
        var configuration = ChannelMapping.ToConfiguration(request.Settings, out var mappingProblems);

        if (mappingProblems.Count > 0)
        {
            await AuditAsync("UpdateChannelDraft", request.ChannelName, false, mappingProblems, context.CancellationToken);

            return ToChannelResponse(false, mappingProblems, channelId);
        }

        var result = await updateChannelDraft.HandleAsync(
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
        var channelId = await RequireChannelIdAsync("PublishChannelRevision", request.ChannelName, context.CancellationToken);

        var result = await publishChannelRevision.HandleAsync(
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
        var channelId = await RequireChannelIdAsync("ActivateChannelRevision", request.ChannelName, context.CancellationToken);

        var result = await activateChannelRevision.HandleAsync(
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
        var channelId = await RequireChannelIdAsync("RollbackChannelRevision", request.ChannelName, context.CancellationToken);

        var result = await rollbackChannelRevision.HandleAsync(
            new Channels.RollbackChannelRevisionCommand(channelId),
            context.CancellationToken);

        var succeeded = result.Problems.Count == 0;

        await AuditAsync("RollbackChannelRevision", request.ChannelName, succeeded, result.Problems, context.CancellationToken);

        return ToChannelResponse(succeeded, result.Problems, channelId, result.RevisionNumber);
    }

    public override async Task<GetChannelHealthResponse> GetChannelHealth(
        GetChannelHealthRequest request,
        ServerCallContext context)
    {
        var result = await getChannelHealth.HandleAsync(
            new GetChannelHealthQuery(request.ChannelName),
            context.CancellationToken);

        if (result.Health is null)
        {
            // Отказ уже записан в аудит срезом: здесь остаётся только код gRPC / HTTP.
            throw new RpcException(new Status(StatusCode.NotFound, "channel not found"));
        }

        return ToHealthResponse(result.Health);
    }

    /// <summary>
    /// Отображает состояние канала в proto-сообщение. Значение секрета в ответ не попадает: срез
    /// возвращает только имя ключа конфигурации.
    /// </summary>
    private static GetChannelHealthResponse ToHealthResponse(ChannelHealthDetails health)
    {
        var response = new GetChannelHealthResponse
        {
            ChannelName = health.ChannelName,
            State = health.State,
            ActiveRevision = health.ActiveRevision,
            SecretConfigured = health.SecretConfigured,
            SecretConfigKey = health.SecretConfigKey,
            CheckedAt = Timestamp.FromDateTimeOffset(health.CheckedAt),
        };

        if (health.CertificateExpiresAt is { } expiresAt)
        {
            response.CertificateExpiresAt = Timestamp.FromDateTimeOffset(expiresAt);
        }

        response.Errors.AddRange(health.Errors.Select(error => new ChannelHealthError
        {
            OccurredAt = Timestamp.FromDateTimeOffset(error.OccurredAt),
            Direction = error.Direction,
            ErrorCode = error.ErrorCode,
            HttpStatus = error.HttpStatus,
            Outcome = error.Outcome,
        }));

        return response;
    }

    private static int? Optional(int revision) => revision <= 0 ? null : revision;
}
