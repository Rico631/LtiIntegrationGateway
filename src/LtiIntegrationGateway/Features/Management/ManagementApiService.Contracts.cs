using Grpc.Core;
using LtiIntegrationGateway.Contracts;
using LtiIntegrationGateway.Contracts.Management.V1;
using LtiIntegrationGateway.Features.Contracts.ActivateContractVersion;
using LtiIntegrationGateway.Features.Contracts.GetContractVersion;
using LtiIntegrationGateway.Features.Contracts.ListContractOperations;
using LtiIntegrationGateway.Features.Contracts.PublishContractVersion;
using LtiIntegrationGateway.Features.Contracts.UpdateContractOperationSettings;
using LtiIntegrationGateway.Features.Contracts.UpdateContractVersionSettings;
using LtiIntegrationGateway.Features.Contracts.UploadContractVersion;

namespace LtiIntegrationGateway.Features.Management;

/// <summary>Операции версий OpenAPI-контрактов.</summary>
public sealed partial class ManagementApiService
{
    public override async Task<ContractOperationResponse> UploadInboundContractVersion(
        UploadContractRequest request,
        ServerCallContext context)
    {
        var problems = MessageContractValidator.ValidateUploadContract(request.ChannelName, request.Document);

        if (problems.Count > 0)
        {
            return ToContractResponse(false, 0, null, problems, []);
        }

        var result = await uploadContractVersion.HandleAsync(
            new UploadContractVersionCommand(request.ChannelName, request.Document),
            context.CancellationToken);

        return ToContractResponse(result.Problems.Count == 0, result.Version, result.SourceFormat, result.Problems, result.Warnings);
    }

    public override async Task<ContractOperationResponse> PublishInboundContractVersion(
        PublishContractVersionRequest request,
        ServerCallContext context)
    {
        var problems = MessageContractValidator.ValidateContractVersionCommand(request.ChannelName, request.Version);

        if (problems.Count > 0)
        {
            return ToContractResponse(false, request.Version, null, problems, []);
        }

        var result = await publishContractVersion.HandleAsync(
            new PublishContractVersionCommand(request.ChannelName, request.Version),
            context.CancellationToken);

        ThrowIfMissing(result.Version, result.Problems);

        return ToContractResponse(result.Problems.Count == 0, result.Version, null, result.Problems, []);
    }

    public override async Task<ContractOperationResponse> ActivateInboundContractVersion(
        ActivateContractVersionRequest request,
        ServerCallContext context)
    {
        var problems = MessageContractValidator.ValidateContractVersionCommand(request.ChannelName, request.Version);

        if (problems.Count > 0)
        {
            return ToContractResponse(false, request.Version, null, problems, []);
        }

        var result = await activateContractVersion.HandleAsync(
            new ActivateContractVersionCommand(request.ChannelName, request.Version),
            context.CancellationToken);

        ThrowIfMissing(result.Version, result.Problems);

        return ToContractResponse(result.Problems.Count == 0, result.Version, null, result.Problems, []);
    }

    public override async Task<ListContractOperationsResponse> ListInboundContractOperations(
        ListContractOperationsRequest request,
        ServerCallContext context)
    {
        var result = await listContractOperations.HandleAsync(
            new ListContractOperationsQuery(request.ChannelName, request.Version),
            context.CancellationToken);

        if (result.Problems.Count > 0)
        {
            await AuditAsync(
                "ListContractOperations",
                $"{request.ChannelName}:v{request.Version}",
                false,
                result.Problems,
                context.CancellationToken);

            throw new RpcException(new Status(StatusCode.NotFound, "contract version not found"));
        }

        await AuditAsync(
            "ListContractOperations",
            $"{request.ChannelName}:v{request.Version}",
            true,
            [],
            context.CancellationToken);

        var response = new ListContractOperationsResponse();

        response.Operations.AddRange(result.Operations.Select(ToOperationInfo));

        return response;
    }

    public override async Task<GetContractVersionResponse> GetInboundContractVersion(
        GetContractVersionRequest request,
        ServerCallContext context)
    {
        var target = $"{request.ChannelName}:v{request.Version}";

        var result = await getContractVersion.HandleAsync(
            new GetContractVersionQuery(request.ChannelName, request.Version),
            context.CancellationToken);

        if (result.ContractVersion is null)
        {
            await AuditAsync("GetContractVersion", target, false, result.Problems, context.CancellationToken);

            throw new RpcException(new Status(StatusCode.NotFound, "contract version not found"));
        }

        await AuditAsync("GetContractVersion", target, true, [], context.CancellationToken);

        var details = result.ContractVersion;
        var response = new GetContractVersionResponse
        {
            Version = details.Version,
            Status = details.Status,
            Settings = ToSettings(details.Settings),
        };

        response.Operations.AddRange(details.Operations.Select(ToOperationInfo));

        return response;
    }

    public override async Task<ContractOperationResponse> UpdateInboundContractVersionSettings(
        UpdateContractVersionSettingsRequest request,
        ServerCallContext context)
    {
        var problems = MessageContractValidator.ValidateUpdateContractVersionSettings(
            request.ChannelName,
            request.Version,
            request.Settings).ToList();
        var authType = ChannelMapping.ParseAuthType(request.Settings?.AuthType, problems);

        if (problems.Count > 0)
        {
            await AuditAsync(
                "UpdateContractVersionSettings",
                $"{request.ChannelName}:v{request.Version}",
                false,
                problems,
                context.CancellationToken);

            return ToContractResponse(false, request.Version, null, problems, []);
        }

        var settings = new LtiIntegrationGateway.Features.Contracts.ContractVersionSettings(
            request.Settings!.RecipientQueue,
            authType,
            request.Settings.ConfigKey,
            request.Settings.HeaderName,
            request.Settings.UserName,
            request.Settings.RateLimitPermit > 0 ? request.Settings.RateLimitPermit : null,
            request.Settings.RateLimitWindowSeconds > 0 ? request.Settings.RateLimitWindowSeconds : null);

        var result = await updateContractVersionSettings.HandleAsync(
            new UpdateContractVersionSettingsCommand(request.ChannelName, request.Version, settings),
            context.CancellationToken);

        ThrowIfMissing(result.Version, result.Problems);

        return ToContractResponse(result.Problems.Count == 0, result.Version, null, result.Problems, []);
    }

    public override async Task<ContractOperationResponse> UpdateInboundContractOperationSettings(
        UpdateContractOperationSettingsRequest request,
        ServerCallContext context)
    {
        var problems = MessageContractValidator.ValidateUpdateContractOperationSettings(
            request.ChannelName,
            request.Version,
            request.OperationId);

        if (problems.Count > 0)
        {
            return ToContractResponse(false, request.Version, null, problems, []);
        }

        // Presence в proto3 optional различает «поле не передано» и «передано пустое/ноль»:
        // отсутствующее поле не меняет настройку.
        var patch = new ContractOperationSettingsPatch(
            request.HasEnabled ? request.Enabled : null,
            request.HasRecipientQueue ? request.RecipientQueue : null);

        var result = await updateContractOperationSettings.HandleAsync(
            new UpdateContractOperationSettingsCommand(
                request.ChannelName,
                request.Version,
                request.OperationId,
                patch),
            context.CancellationToken);

        if (result.NotFound)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "contract operation not found"));
        }

        return ToContractResponse(result.Problems.Count == 0, result.Version, null, result.Problems, []);
    }

    private static ContractOperationInfo ToOperationInfo(ContractOperationDetails operation) => new()
    {
        HttpMethod = operation.HttpMethod,
        Template = operation.Template,
        OperationId = operation.OperationId ?? string.Empty,
        Enabled = operation.Enabled,
        RecipientQueue = operation.RecipientQueue ?? string.Empty,
    };

    private static LtiIntegrationGateway.Contracts.Management.V1.ContractVersionSettings ToSettings(
        LtiIntegrationGateway.Features.Contracts.ContractVersionSettings settings) => new()
    {
        RecipientQueue = settings.RecipientQueue ?? string.Empty,
        AuthType = ChannelMapping.FormatAuthType(settings.AuthType),
        ConfigKey = settings.ConfigKey ?? string.Empty,
        HeaderName = settings.HeaderName ?? string.Empty,
        UserName = settings.UserName ?? string.Empty,
        RateLimitPermit = settings.RateLimitPermit ?? 0,
        RateLimitWindowSeconds = settings.RateLimitWindowSeconds ?? 0,
    };

    private static ContractOperationResponse ToContractResponse(
        bool succeeded,
        int? version,
        string? sourceFormat,
        IReadOnlyList<string> problems,
        IReadOnlyList<string> warnings)
    {
        var response = new ContractOperationResponse
        {
            Succeeded = succeeded,
            Version = version ?? 0,
            SourceFormat = sourceFormat ?? string.Empty,
        };

        response.Problems.AddRange(problems.Select(ToProblem));
        response.Problems.AddRange(warnings.Select(ToWarning));

        return response;
    }

    private static void ThrowIfMissing(int? version, IReadOnlyList<string> problems)
    {
        if (version is null || problems.Any(problem => problem.Contains("не найдена", StringComparison.Ordinal)))
        {
            throw new RpcException(new Status(StatusCode.NotFound, "contract version not found"));
        }
    }
}
