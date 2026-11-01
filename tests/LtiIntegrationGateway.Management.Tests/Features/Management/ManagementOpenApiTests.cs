using System.Net;
using System.Text.Json.Nodes;
using LtiIntegrationGateway.Contracts.Management.V1;
using LtiIntegrationGateway.Tests.TestSupport;

namespace LtiIntegrationGateway.Tests.Features.Management;

public class ManagementOpenApiTests
{
    private static readonly string[] HttpMethods = ["get", "post", "put", "patch", "delete"];

    [Test]
    public async Task Document_describes_every_transcoding_route()
    {
        using var factory = ManagementHostFactory.WithStorage(ManagementApi.Settings());
        using var client = ManagementApi.CreateClient(factory, apiKey: null);

        var response = await client.GetAsync("/openapi/management.json");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var openApi = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

        await Assert.That(openApi["openapi"]!.GetValue<string>().StartsWith("3.0", StringComparison.Ordinal)).IsTrue();

        var paths = openApi["paths"]!.AsObject();
        var operationCount = paths
            .SelectMany(path => path.Value!.AsObject())
            .Count(operation => HttpMethods.Contains(operation.Key));

        await Assert.That(operationCount).IsEqualTo(ManagementService.Descriptor.Methods.Count);
    }

    [Test]
    public async Task Document_follows_proto_contract()
    {
        using var factory = ManagementHostFactory.WithStorage(ManagementApi.Settings());
        using var client = ManagementApi.CreateClient(factory, apiKey: null);

        var response = await client.GetAsync("/openapi/management.json");
        var openApi = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        var paths = openApi["paths"]!.AsObject();

        var channels = paths["/v1/integrations/{integration}/outbound-channels"]!.AsObject();
        await Assert.That(channels["get"]!["operationId"]!.GetValue<string>()).IsEqualTo("ListOutboundChannels");
        await Assert.That(channels["post"]!["requestBody"]!["content"]!["application/json"]!["schema"]!["$ref"]!
            .GetValue<string>()).IsEqualTo("#/components/schemas/CreateChannelRequest");

        var channel = paths["/v1/integrations/{integration}/outbound-channels/{channel_name}"]!.AsObject();
        var parameters = channel["get"]!["parameters"]!.AsArray();
        await Assert.That(parameters.Any(parameter =>
            parameter!["name"]!.GetValue<string>() == "channel_name" &&
            parameter["in"]!.GetValue<string>() == "path" &&
            parameter["required"]!.GetValue<bool>())).IsTrue();

        // Описания операций и полей берутся из комментариев management_api.proto.
        await Assert.That(channels["post"]!["description"]!.GetValue<string>()
            .Contains("Создаёт канал", StringComparison.Ordinal)).IsTrue();

        var settings = openApi["components"]!["schemas"]!["ChannelSettings"]!;
        await Assert.That(settings["properties"]!["baseUrl"]!["description"]!.GetValue<string>()
            .Contains("Базовый URL", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task Swagger_ui_is_served_without_api_key()
    {
        using var factory = ManagementHostFactory.WithStorage(ManagementApi.Settings(apiKey: null));
        using var client = ManagementApi.CreateClient(factory, apiKey: null);

        var response = await client.GetAsync("/swagger/index.html");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("text/html");
    }
}
