using System.Net;
using System.Text;
using System.Text.Json;
using LtiIntegrationGateway.Contracts.Management.V1;

namespace LtiIntegrationGateway.Tests.TestSupport;

/// <summary>
/// Шаги работы с контрактами в тестах: канал, загрузка документа, публикация и активация версии,
/// настройки версии и операции. Документ OpenAPI собирается из перечисления операций.
/// </summary>
public static class ContractOperations
{
    public const string ChannelName = "bank-a";
    public const string VersionQueue = "dpv.inbox";

    /// <summary>Документ OpenAPI 3.0 из перечисления операций; operationId опускается, если равен null.</summary>
    public static string Document(params (string Method, string Path, string? OperationId)[] operations)
    {
        var builder = new StringBuilder();
        builder.Append("""{ "openapi": "3.0.3", "info": { "title": "sample", "version": "1.0.0" }, "paths": {""");

        for (var index = 0; index < operations.Length; index++)
        {
            var (method, path, operationId) = operations[index];

            if (index > 0)
            {
                builder.Append(',');
            }

            builder
                .Append(JsonSerializer.Serialize(path))
                .Append(": { ")
                .Append(JsonSerializer.Serialize(method.ToLowerInvariant()))
                .Append(": { ");

            if (operationId is not null)
            {
                builder
                    .Append("\"operationId\": ")
                    .Append(JsonSerializer.Serialize(operationId))
                    .Append(", ");
            }

            builder.Append("\"responses\": { \"202\": { \"description\": \"accepted\" } } } }");
        }

        builder.Append("} }");

        return builder.ToString();
    }

    public static async Task CreateChannelAsync(HttpClient client, string name = ChannelName)
    {
        var response = await client.PostAsync("/v1/channels", ManagementApi.Json(JsonSerializer.Serialize(new
        {
            name,
            settings = new
            {
                baseUrl = "https://" + name + ".example",
                authType = "none",
                perAttemptTimeoutMs = 5000,
                overallCapMs = 30000,
                retryPolicy = new[]
                {
                    new { match = "transport_error", statusCodes = Array.Empty<int>(), delaysMs = new[] { 100 } },
                },
                allowedMethods = new[] { "GET", "POST" },
                allowedPaths = new[] { "/api/contracts" },
            },
        })));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    public static async Task<int> UploadAsync(
        ManagementService.ManagementServiceClient client,
        string document,
        string channelName = ChannelName)
    {
        var upload = await client.UploadContractAsync(
            new UploadContractRequest { ChannelName = channelName, Document = document },
            ManagementApi.Call());

        await Assert.That(upload.Succeeded).IsTrue();

        return upload.Version;
    }

    public static async Task SetVersionQueueAsync(
        ManagementService.ManagementServiceClient client,
        int version,
        string queue = VersionQueue,
        string channelName = ChannelName)
    {
        var update = await client.UpdateContractVersionSettingsAsync(
            new UpdateContractVersionSettingsRequest
            {
                ChannelName = channelName,
                Version = version,
                Settings = new ContractVersionSettings { RecipientQueue = queue },
            },
            ManagementApi.Call());

        await Assert.That(update.Succeeded).IsTrue();
    }

    public static async Task<ContractOperationResponse> PublishAsync(
        ManagementService.ManagementServiceClient client,
        int version,
        string channelName = ChannelName) =>
        await client.PublishContractVersionAsync(
            new PublishContractVersionRequest { ChannelName = channelName, Version = version },
            ManagementApi.Call());

    public static async Task<ContractOperationResponse> ActivateAsync(
        ManagementService.ManagementServiceClient client,
        int version,
        string channelName = ChannelName) =>
        await client.ActivateContractVersionAsync(
            new ActivateContractVersionRequest { ChannelName = channelName, Version = version },
            ManagementApi.Call());

    /// <summary>Изменение настроек операции: передаются только заданные поля (presence в proto3 optional).</summary>
    public static async Task<ContractOperationResponse> PatchAsync(
        ManagementService.ManagementServiceClient client,
        int version,
        string operationId,
        bool? enabled = null,
        string? recipientQueue = null,
        int? rateLimitPermit = null,
        int? rateLimitWindowSeconds = null,
        string channelName = ChannelName)
    {
        var request = new UpdateContractOperationSettingsRequest
        {
            ChannelName = channelName,
            Version = version,
            OperationId = operationId,
        };

        if (enabled is { } enabledValue)
        {
            request.Enabled = enabledValue;
        }

        if (recipientQueue is not null)
        {
            request.RecipientQueue = recipientQueue;
        }

        if (rateLimitPermit is { } permit)
        {
            request.RateLimitPermit = permit;
        }

        if (rateLimitWindowSeconds is { } window)
        {
            request.RateLimitWindowSeconds = window;
        }

        return await client.UpdateContractOperationSettingsAsync(request, ManagementApi.Call());
    }

    public static async Task<ListContractOperationsResponse> ReadOperationsAsync(
        ManagementService.ManagementServiceClient client,
        int version,
        string channelName = ChannelName) =>
        await client.ListContractOperationsAsync(
            new ListContractOperationsRequest { ChannelName = channelName, Version = version },
            ManagementApi.Call());

    /// <summary>Публикация и активация версии с очередью получателя из настроек версии.</summary>
    public static async Task PublishAndActivateAsync(
        ManagementService.ManagementServiceClient client,
        string document,
        int version,
        string channelName = ChannelName)
    {
        await UploadAsync(client, document, channelName);
        await SetVersionQueueAsync(client, version, channelName: channelName);

        var published = await PublishAsync(client, version, channelName);

        await Assert.That(published.Succeeded).IsTrue();

        var activated = await ActivateAsync(client, version, channelName);

        await Assert.That(activated.Succeeded).IsTrue();
    }
}
