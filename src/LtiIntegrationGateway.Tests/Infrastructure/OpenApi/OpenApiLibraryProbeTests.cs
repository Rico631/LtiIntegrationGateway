using System.Text;
using Microsoft.OpenApi;

namespace LtiIntegrationGateway.Tests.Infrastructure.OpenApi;

/// <summary>
/// Пробный тест, фиксирующий фактическое поведение библиотеки чтения OpenAPI: возможность загрузки
/// документа из потока, доступ к диагностике и поддержку Swagger 2.0.
/// </summary>
public class OpenApiLibraryProbeTests
{
    private const string OpenApi30 = """
    {
      "openapi": "3.0.3",
      "info": { "title": "probe", "version": "1.0.0" },
      "paths": {
        "/callbacks/contracts": {
          "post": { "operationId": "receiveContract", "responses": { "200": { "description": "ok" } } }
        }
      }
    }
    """;

    private const string Swagger20 = """
    {
      "swagger": "2.0",
      "info": { "title": "probe", "version": "1.0.0" },
      "paths": {
        "/callbacks/contracts": {
          "post": { "operationId": "receiveContract", "responses": { "200": { "description": "ok" } } }
        }
      }
    }
    """;

    [Test]
    public async Task OpenApi_30_document_is_loaded_with_paths()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(OpenApi30));

        var result = await OpenApiDocument.LoadAsync(stream);

        await Assert.That(result.Document).IsNotNull();
        await Assert.That(result.Document?.Paths?.Count).IsEqualTo(1);
        await Assert.That(result.Diagnostic?.Errors.Count ?? 0).IsEqualTo(0);
    }

    [Test]
    public async Task Swagger_20_document_is_loaded_with_paths()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Swagger20));

        var result = await OpenApiDocument.LoadAsync(stream);

        await Assert.That(result.Document).IsNotNull();
        await Assert.That(result.Document?.Paths?.Count).IsEqualTo(1);
        await Assert.That(result.Diagnostic?.SpecificationVersion).IsEqualTo(OpenApiSpecVersion.OpenApi2_0);
    }
}
