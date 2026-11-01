using System.Text.Json.Nodes;

namespace LtiIntegrationGateway.Features.Documentation;

/// <summary>Объединение входящих контрактов интеграции в один OpenAPI-документ.</summary>
public static class InboundContractDocument
{
    private static readonly string[] OperationMethods =
        ["get", "put", "post", "delete", "options", "head", "patch", "trace"];

    /// <summary>Добавляет пути и схемы контракта канала; операции помечаются тегом канала.</summary>
    public static void Merge(string channel, string document, JsonObject paths, JsonObject schemas)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(schemas);

        if (JsonNode.Parse(document) is not JsonObject root || root["paths"] is not JsonObject documentPaths)
        {
            return;
        }

        foreach (var (template, item) in documentPaths)
        {
            // Пересечение путей в одной интеграции отклоняется валидацией набора маршрутов.
            if (item is not JsonObject pathItem || paths.ContainsKey(template))
            {
                continue;
            }

            foreach (var method in OperationMethods)
            {
                if (pathItem[method] is JsonObject operation)
                {
                    operation["tags"] = new JsonArray(channel);
                }
            }

            paths[template] = pathItem.DeepClone();
        }

        if (root["components"]?["schemas"] is JsonObject documentSchemas)
        {
            foreach (var (name, schema) in documentSchemas)
            {
                // shortcut: одноимённые схемы разных каналов не объединяются, остаётся первая;
                // при реальном конфликте имён схемы придётся называть по-разному в контрактах.
                if (schema is not null && !schemas.ContainsKey(name))
                {
                    schemas[name] = schema.DeepClone();
                }
            }
        }
    }
}
