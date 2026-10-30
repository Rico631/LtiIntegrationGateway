using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;

namespace LtiIntegrationGateway.Infrastructure.OpenApi;

/// <summary>
/// Чтение OpenAPI-документа: определение версии, загрузка через <c>Microsoft.OpenApi</c> (проверка
/// синтаксиса и нормализация) и fail-closed проверки по исходному JSON.
/// </summary>
public sealed class MicrosoftOpenApiDocumentReader : IOpenApiDocumentReader
{
    public async Task<OpenApiReadResult> ReadAsync(
        string document,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(document))
        {
            return Failure(OpenApiSourceFormat.OpenApi30, "document: документ пуст");
        }

        JsonNode? root;

        try
        {
            root = JsonNode.Parse(document);
        }
        catch (JsonException exception)
        {
            return Failure(OpenApiSourceFormat.OpenApi30, $"document: JSON не разбирается ({exception.Message})");
        }

        if (root is not JsonObject rootObject)
        {
            return Failure(OpenApiSourceFormat.OpenApi30, "document: ожидается JSON-объект");
        }

        var format = DetectFormat(rootObject, out var versionProblem);

        if (format is null)
        {
            return Failure(OpenApiSourceFormat.OpenApi30, versionProblem!);
        }

        var problems = new List<string>();
        var warnings = new List<string>();

        OpenApiDocumentValidator.Validate(rootObject, problems, warnings);
        await LoadLibraryAsync(document, problems, cancellationToken);

        return new OpenApiReadResult(
            format.Value,
            OpenApiDocumentValidator.ExtractOperations(rootObject),
            problems.Distinct(StringComparer.Ordinal).ToArray(),
            warnings.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static async Task LoadLibraryAsync(
        string document,
        List<string> problems,
        CancellationToken cancellationToken)
    {
        try
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(document));

            var result = await OpenApiDocument.LoadAsync(stream);

            if (result.Document is not null)
            {
                return;
            }

            if (result.Diagnostic is { } diagnostic)
            {
                problems.AddRange(diagnostic.Errors.Select(error => $"document: {error.Message}"));
            }
            else
            {
                problems.Add("document: документ не прочитан библиотекой");
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            problems.Add($"document: документ не читается библиотекой ({exception.Message})");
        }
    }

    private static OpenApiSourceFormat? DetectFormat(JsonObject root, out string? problem)
    {
        problem = null;

        if (root["openapi"] is JsonValue openApiValue &&
            openApiValue.TryGetValue<string>(out var openApiVersion))
        {
            if (openApiVersion.StartsWith("3.0", StringComparison.Ordinal))
            {
                return OpenApiSourceFormat.OpenApi30;
            }

            problem = $"version: OpenAPI '{openApiVersion}' не поддерживается, требуется 3.0";

            return null;
        }

        if (root["swagger"] is JsonValue swaggerValue &&
            swaggerValue.TryGetValue<string>(out var swaggerVersion))
        {
            if (string.Equals(swaggerVersion, "2.0", StringComparison.Ordinal))
            {
                return OpenApiSourceFormat.Swagger20;
            }

            problem = $"version: Swagger '{swaggerVersion}' не поддерживается";

            return null;
        }

        problem = "version: документ не содержит поля 'openapi' или 'swagger'";

        return null;
    }

    private static OpenApiReadResult Failure(OpenApiSourceFormat format, string problem) =>
        new(format, [], [problem], []);
}
