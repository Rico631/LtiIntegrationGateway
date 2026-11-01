using System.Text.Json.Nodes;
using LtiIntegrationGateway.Features.Documentation;

namespace LtiIntegrationGateway.Tests.Features.Documentation;

public class InboundContractDocumentTests
{
    private const string AsyncDocument =
        """
        {
          "openapi": "3.0.3",
          "paths": { "/order": { "post": { "operationId": "receiveOrder" } } },
          "components": { "schemas": { "Order": { "type": "object" } } }
        }
        """;

    private const string SyncDocument =
        """
        {
          "openapi": "3.0.3",
          "paths": { "/order/{orderid}": { "get": { "operationId": "getOrder" } } },
          "components": { "schemas": { "Order": { "type": "string" } } }
        }
        """;

    [Test]
    public async Task Merge_collects_paths_of_all_channels_and_marks_them_with_channel_tag()
    {
        var paths = new JsonObject();
        var schemas = new JsonObject();

        InboundContractDocument.Merge("orders-async", AsyncDocument, paths, schemas);
        InboundContractDocument.Merge("orders-sync", SyncDocument, paths, schemas);

        await Assert.That(string.Join(", ", paths.Select(path => path.Key))).IsEqualTo("/order, /order/{orderid}");
        await Assert.That(paths["/order"]!["post"]!["tags"]![0]!.GetValue<string>()).IsEqualTo("orders-async");
        await Assert.That(paths["/order/{orderid}"]!["get"]!["tags"]![0]!.GetValue<string>()).IsEqualTo("orders-sync");
        await Assert.That(schemas["Order"]!["type"]!.GetValue<string>()).IsEqualTo("object");
    }
}
