using Grpc.Core;
using Grpc.Net.Client;

namespace LtiIntegrationGateway.Tests.TestSupport;

/// <summary>Доступ к управляющему API в тестах: ключ, REST-клиент и in-process gRPC-канал.</summary>
public static class ManagementApi
{
    public const string ApiKey = "test-management-key";
    public const string HeaderName = "X-Management-Api-Key";

    public static Dictionary<string, string?> Settings(string? apiKey = ApiKey) => new()
    {
        ["Management:ApiKey"] = apiKey ?? string.Empty,
        ["Management:HeaderName"] = HeaderName,
    };

    public static HttpClient CreateClient(GatewayWebApplicationFactory factory, string? apiKey = ApiKey)
    {
        var client = factory.CreateClient();

        if (!string.IsNullOrEmpty(apiKey))
        {
            client.DefaultRequestHeaders.Add(HeaderName, apiKey);
        }

        return client;
    }

    public static GrpcChannel CreateChannel(GatewayWebApplicationFactory factory) =>
        GrpcChannel.ForAddress(
            factory.Server.BaseAddress,
            new GrpcChannelOptions { HttpHandler = factory.Server.CreateHandler() });

    public static CallOptions Call(string? apiKey = ApiKey)
    {
        var metadata = new Metadata();

        if (!string.IsNullOrEmpty(apiKey))
        {
            metadata.Add(HeaderName, apiKey);
        }

        return new CallOptions(headers: metadata);
    }

    public static StringContent Json(string json) => new(json, System.Text.Encoding.UTF8, "application/json");
}
