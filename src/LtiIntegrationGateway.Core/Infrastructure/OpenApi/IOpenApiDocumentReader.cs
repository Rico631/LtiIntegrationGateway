namespace LtiIntegrationGateway.Infrastructure.OpenApi;

/// <summary>Формат исходного документа контракта.</summary>
public enum OpenApiSourceFormat
{
    OpenApi30 = 1,
    Swagger20 = 2,
}

/// <summary>Операция, извлечённая из документа контракта.</summary>
public sealed record OpenApiOperationDefinition(string HttpMethod, string Template, string? OperationId);

/// <summary>Результат чтения и проверки документа: операции, схемы тела, проблемы и предупреждения.</summary>
public sealed record OpenApiReadResult(
    OpenApiSourceFormat Format,
    IReadOnlyList<OpenApiOperationDefinition> Operations,
    IReadOnlyDictionary<string, string> RequestSchemas,
    IReadOnlyList<string> Problems,
    IReadOnlyList<string> Warnings)
{
    public bool Succeeded => Problems.Count == 0;
}

/// <summary>Чтение OpenAPI-документа с проверкой поддерживаемых конструкций.</summary>
public interface IOpenApiDocumentReader
{
    Task<OpenApiReadResult> ReadAsync(string document, CancellationToken cancellationToken = default);
}
