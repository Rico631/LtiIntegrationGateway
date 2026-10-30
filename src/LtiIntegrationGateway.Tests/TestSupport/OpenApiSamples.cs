namespace LtiIntegrationGateway.Tests.TestSupport;

/// <summary>Документы-образцы OpenAPI для проверок разбора и загрузки контрактов.</summary>
public static class OpenApiSamples
{
    public static string OpenApi30(params (string Method, string Path, string? OperationId)[] operations)
    {
        var paths = string.Join(",", operations.Select(operation =>
            "\"" + operation.Path + "\": {" +
            "\"" + operation.Method.ToLowerInvariant() + "\": {" +
            "\"operationId\": \"" + operation.OperationId + "\"," +
            "\"responses\": { \"200\": { \"description\": \"ok\" } } } }"));

        return """
        {
          "openapi": "3.0.3",
          "info": { "title": "sample", "version": "1.0.0" },
          "paths": { PATHS }
        }
        """.Replace("PATHS", paths, StringComparison.Ordinal);
    }

    public static string OpenApi30WithPath(string method, string path) =>
        OpenApi30((method, path, "receiveCallback"));

    public const string Swagger20 = """
    {
      "swagger": "2.0",
      "info": { "title": "sample", "version": "1.0.0" },
      "paths": {
        "/callbacks/contracts": {
          "post": { "operationId": "receiveContract", "responses": { "200": { "description": "ok" } } }
        }
      }
    }
    """;

    public const string OpenApi31 = """
    {
      "openapi": "3.1.0",
      "info": { "title": "sample", "version": "1.0.0" },
      "paths": {
        "/callbacks/contracts": {
          "post": { "responses": { "200": { "description": "ok" } } }
        }
      }
    }
    """;

    public const string ForbiddenKeyword = """
    {
      "openapi": "3.0.3",
      "info": { "title": "sample", "version": "1.0.0" },
      "paths": {
        "/callbacks/contracts": {
          "post": {
            "requestBody": {
              "content": {
                "application/json": {
                  "schema": { "type": "object", "if": { "required": ["a"] } }
                }
              }
            },
            "responses": { "200": { "description": "ok" } }
          }
        }
      }
    }
    """;

    public const string ExternalReference = """
    {
      "openapi": "3.0.3",
      "info": { "title": "sample", "version": "1.0.0" },
      "paths": {
        "/callbacks/contracts": {
          "post": {
            "requestBody": {
              "content": {
                "application/json": {
                  "schema": { "$ref": "https://example.com/schema.json" }
                }
              }
            },
            "responses": { "200": { "description": "ok" } }
          }
        }
      }
    }
    """;

    public const string ReferenceWithSibling = """
    {
      "openapi": "3.0.3",
      "info": { "title": "sample", "version": "1.0.0" },
      "paths": {},
      "components": {
        "schemas": {
          "Contract": { "type": "object" },
          "Wrapped": { "$ref": "#/components/schemas/Contract", "nullable": true }
        }
      }
    }
    """;

    public const string UnknownFormat = """
    {
      "openapi": "3.0.3",
      "info": { "title": "sample", "version": "1.0.0" },
      "paths": {},
      "components": {
        "schemas": {
          "Account": { "type": "string", "format": "bank-account" }
        }
      }
    }
    """;

    public const string MultipleProblems = """
    {
      "openapi": "3.0.3",
      "info": { "title": "sample", "version": "1.0.0" },
      "paths": {
        "/callbacks/contracts": {
          "post": {
            "requestBody": {
              "content": {
                "application/json": {
                  "schema": { "type": "object", "const": 1, "$ref": "https://example.com/schema.json" }
                }
              }
            },
            "responses": { "200": { "description": "ok" } }
          }
        }
      }
    }
    """;
}
