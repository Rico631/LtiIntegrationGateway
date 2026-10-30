using LtiIntegrationGateway.Infrastructure.OpenApi;
using LtiIntegrationGateway.Tests.TestSupport;

namespace LtiIntegrationGateway.Tests.Infrastructure.OpenApi;

public class OpenApiDocumentReaderTests
{
    private static MicrosoftOpenApiDocumentReader Reader() => new();

    [Test]
    public async Task OpenApi_30_document_is_accepted_with_operations()
    {
        var result = await Reader().ReadAsync(
            OpenApiSamples.OpenApi30(("POST", "/callbacks/contracts", "receiveContract")));

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Format).IsEqualTo(OpenApiSourceFormat.OpenApi30);
        await Assert.That(result.Operations.Single().HttpMethod).IsEqualTo("POST");
        await Assert.That(result.Operations.Single().Template).IsEqualTo("/callbacks/contracts");
        await Assert.That(result.Operations.Single().OperationId).IsEqualTo("receiveContract");
    }

    [Test]
    public async Task Swagger_20_document_is_accepted_and_marked_as_swagger()
    {
        var result = await Reader().ReadAsync(OpenApiSamples.Swagger20);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Format).IsEqualTo(OpenApiSourceFormat.Swagger20);
        await Assert.That(result.Operations.Single().Template).IsEqualTo("/callbacks/contracts");
    }

    [Test]
    public async Task OpenApi_31_document_is_rejected()
    {
        var result = await Reader().ReadAsync(OpenApiSamples.OpenApi31);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Problems.Any(problem => problem.StartsWith("version:", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Forbidden_keyword_is_rejected_with_path()
    {
        var result = await Reader().ReadAsync(OpenApiSamples.ForbiddenKeyword);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Problems.Any(problem => problem.Contains("'if'", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task External_reference_is_rejected()
    {
        var result = await Reader().ReadAsync(OpenApiSamples.ExternalReference);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Problems.Any(problem =>
            problem.Contains("https://example.com/schema.json", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Reference_with_sibling_fields_is_rejected()
    {
        var result = await Reader().ReadAsync(OpenApiSamples.ReferenceWithSibling);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Problems.Any(problem =>
            problem.Contains("поля рядом с $ref", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Unknown_format_produces_warning_only()
    {
        var result = await Reader().ReadAsync(OpenApiSamples.UnknownFormat);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Warnings.Any(warning =>
            warning.Contains("bank-account", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Multiple_problems_are_reported_together()
    {
        var result = await Reader().ReadAsync(OpenApiSamples.MultipleProblems);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Problems.Count).IsGreaterThanOrEqualTo(2);
    }

    [Test]
    public async Task Invalid_json_is_rejected()
    {
        var result = await Reader().ReadAsync("{ not json");

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Problems.Any(problem => problem.StartsWith("document:", StringComparison.Ordinal))).IsTrue();
    }
}
