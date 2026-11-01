using LtiIntegrationGateway.Infrastructure.OpenApi;
using LtiIntegrationGateway.Tests.TestSupport;

namespace LtiIntegrationGateway.Tests.Infrastructure.OpenApi;

public class OpenApiSchemaExtractionTests
{
    private static MicrosoftOpenApiDocumentReader Reader() => new();

    [Test]
    public async Task Inline_schema_is_captured_per_content_type()
    {
        var result = await Reader().ReadAsync(OpenApiSamples.WithInlineBody);

        await Assert.That(result.Succeeded).IsTrue();

        var schemas = result.RequestSchemas["POST /callbacks/contracts"];

        await Assert.That(schemas).Contains("application/json");
        await Assert.That(schemas).Contains("contractNumber");
    }

    [Test]
    public async Task Nested_references_are_inlined()
    {
        var result = await Reader().ReadAsync(OpenApiSamples.WithReferencedBody);

        await Assert.That(result.Succeeded).IsTrue();

        var schemas = result.RequestSchemas["POST /callbacks/contracts"];

        await Assert.That(schemas).DoesNotContain("$ref");
        await Assert.That(schemas).Contains("nested");
        await Assert.That(schemas).Contains("value");
    }

    [Test]
    public async Task Both_content_types_are_captured()
    {
        var result = await Reader().ReadAsync(OpenApiSamples.WithTwoContentTypes);

        await Assert.That(result.Succeeded).IsTrue();

        var schemas = result.RequestSchemas["POST /callbacks/contracts"];

        await Assert.That(schemas).Contains("application/json");
        await Assert.That(schemas).Contains("text/plain");
    }

    [Test]
    public async Task Cyclic_reference_is_rejected()
    {
        var result = await Reader().ReadAsync(OpenApiSamples.WithCyclicReference);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Problems.Any(problem =>
            problem.Contains("циклическая ссылка", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Operation_without_body_has_no_schemas()
    {
        var result = await Reader().ReadAsync(
            OpenApiSamples.OpenApi30(("GET", "/callbacks/contracts", "listContracts")));

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.RequestSchemas.Count).IsEqualTo(0);
    }
}
