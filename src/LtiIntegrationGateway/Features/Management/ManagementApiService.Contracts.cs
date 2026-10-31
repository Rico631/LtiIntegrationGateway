using Grpc.Core;
using LtiIntegrationGateway.Contracts.Management.V1;

namespace LtiIntegrationGateway.Features.Management;

/// <summary>Операции версий OpenAPI-контрактов.</summary>
public sealed partial class ManagementApiService
{
    public override async Task<ContractOperationResponse> UploadContract(
        UploadContractRequest request,
        ServerCallContext context)
    {
        var result = await _uploadContractVersion.HandleAsync(
            new Contracts.UploadContractVersionCommand(request.ChannelName, request.Document),
            context.CancellationToken);

        return ToContractResponse(result.Problems.Count == 0, result.Version, result.SourceFormat, result.Problems, result.Warnings);
    }

    public override async Task<ContractOperationResponse> PublishContractVersion(
        PublishContractVersionRequest request,
        ServerCallContext context)
    {
        var result = await _publishContractVersion.HandleAsync(
            new Contracts.PublishContractVersionCommand(request.ChannelName, request.Version),
            context.CancellationToken);

        ThrowIfMissing(result.Version, result.Problems);

        return ToContractResponse(result.Problems.Count == 0, result.Version, null, result.Problems, []);
    }

    public override async Task<ContractOperationResponse> ActivateContractVersion(
        ActivateContractVersionRequest request,
        ServerCallContext context)
    {
        var result = await _activateContractVersion.HandleAsync(
            new Contracts.ActivateContractVersionCommand(request.ChannelName, request.Version),
            context.CancellationToken);

        ThrowIfMissing(result.Version, result.Problems);

        return ToContractResponse(result.Problems.Count == 0, result.Version, null, result.Problems, []);
    }

    public override async Task<ListContractOperationsResponse> ListContractOperations(
        ListContractOperationsRequest request,
        ServerCallContext context)
    {
        var result = await _listContractOperations.HandleAsync(
            new Contracts.ListContractOperationsQuery(request.ChannelName, request.Version),
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

        response.Operations.AddRange(result.Operations.Select(operation => new ContractOperationInfo
        {
            HttpMethod = operation.HttpMethod,
            Template = operation.Template,
            OperationId = operation.OperationId ?? string.Empty,
        }));

        return response;
    }

    public override async Task<GetContractVersionResponse> GetContractVersion(
        GetContractVersionRequest request,
        ServerCallContext context)
    {
        var target = $"{request.ChannelName}:v{request.Version}";

        var result = await _getContractVersion.HandleAsync(
            new Contracts.GetContractVersionQuery(request.ChannelName, request.Version),
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

        response.Operations.AddRange(details.Operations.Select(operation => new ContractOperationInfo
        {
            HttpMethod = operation.HttpMethod,
            Template = operation.Template,
            OperationId = operation.OperationId ?? string.Empty,
        }));

        return response;
    }

    public override async Task<ContractOperationResponse> UpdateContractVersionSettings(
        UpdateContractVersionSettingsRequest request,
        ServerCallContext context)
    {
        var problems = new List<string>();
        var authType = ChannelMapping.ParseAuthType(request.Settings?.AuthType, problems);

        if (request.Settings is null)
        {
            problems.Add("settings: настройки обязательны");
        }

        if (problems.Count > 0)
        {
            return ToContractResponse(false, request.Version, null, problems, []);
        }

        var settings = new Contracts.ContractVersionSettings(
            request.Settings!.RecipientQueue,
            authType,
            request.Settings.ConfigKey,
            request.Settings.HeaderName,
            request.Settings.UserName,
            request.Settings.RateLimitPermit > 0 ? request.Settings.RateLimitPermit : null,
            request.Settings.RateLimitWindowSeconds > 0 ? request.Settings.RateLimitWindowSeconds : null);

        var result = await _updateContractVersionSettings.HandleAsync(
            new Contracts.UpdateContractVersionSettingsCommand(request.ChannelName, request.Version, settings),
            context.CancellationToken);

        ThrowIfMissing(result.Version, result.Problems);

        return ToContractResponse(result.Problems.Count == 0, result.Version, null, result.Problems, []);
    }

    private static ContractVersionSettings ToSettings(Contracts.ContractVersionSettings settings) => new()
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
        response.Warnings.AddRange(warnings);

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
