using System.Text.Json.Nodes;

namespace LtiIntegrationGateway.Infrastructure.OpenApi;

/// <summary>
/// Проверки документа по принципу fail-closed (ADR §14): поддерживаемые конструкции, локальность
/// <c>$ref</c>, отсутствие полей рядом с <c>$ref</c>, допустимость <c>format</c>.
/// </summary>
internal static class OpenApiDocumentValidator
{
    private static readonly HashSet<string> ForbiddenKeywords = new(StringComparer.Ordinal)
    {
        "if", "then", "else", "const", "patternProperties", "propertyNames", "contains",
        "minContains", "maxContains", "dependentRequired", "dependentSchemas", "dependencies",
        "unevaluatedProperties", "unevaluatedItems", "prefixItems", "$dynamicRef", "$anchor",
        "$recursiveRef",
    };

    private static readonly HashSet<string> KnownFormats = new(StringComparer.Ordinal)
    {
        "date", "date-time", "duration", "email", "uri", "uuid", "byte", "int32", "int64",
        "float", "double", "password",
    };

    private static readonly HashSet<string> HttpMethods = new(StringComparer.Ordinal)
    {
        "get", "put", "post", "delete", "options", "head", "patch", "trace",
    };

    private const int MaxReferenceDepth = 32;

    /// <summary>
    /// Извлекает схемы тела операций: для каждой операции собирается JSON-объект
    /// <c>{ "content-type": schema }</c> с развёрнутыми локальными <c>$ref</c>.
    /// Ключ результата — <c>"METHOD template"</c>.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ExtractRequestSchemas(
        JsonObject root,
        List<string> problems)
    {
        var schemas = new Dictionary<string, string>(StringComparer.Ordinal);

        if (root["paths"] is not JsonObject paths)
        {
            return schemas;
        }

        foreach (var path in paths)
        {
            if (path.Value is not JsonObject pathItem)
            {
                continue;
            }

            foreach (var member in pathItem)
            {
                if (!HttpMethods.Contains(member.Key) || member.Value is not JsonObject operation)
                {
                    continue;
                }

                if (operation["requestBody"] is not JsonObject requestBody ||
                    requestBody["content"] is not JsonObject content)
                {
                    continue;
                }

                var contentSchemas = new JsonObject();

                foreach (var contentType in content)
                {
                    if (contentType.Value is not JsonObject mediaType ||
                        mediaType["schema"] is not JsonObject schema)
                    {
                        continue;
                    }

                    var inlined = InlineReferences(
                        schema,
                        root,
                        depth: 0,
                        references: [],
                        problems,
                        $"paths.{path.Key}.{member.Key}.requestBody.content.{contentType.Key}.schema");

                    if (inlined is JsonObject inlinedObject)
                    {
                        contentSchemas[contentType.Key] = inlinedObject;
                    }
                }

                if (contentSchemas.Count > 0)
                {
                    schemas[$"{member.Key.ToUpperInvariant()} {path.Key}"] = contentSchemas.ToJsonString();
                }
            }
        }

        return schemas;
    }

    private static JsonNode? InlineReferences(
        JsonNode? node,
        JsonObject root,
        int depth,
        HashSet<string> references,
        List<string> problems,
        string path)
    {
        if (node is null)
        {
            return null;
        }

        if (depth > MaxReferenceDepth)
        {
            problems.Add($"schema: превышена глубина вложенности ссылок ({path})");

            return null;
        }

        switch (node)
        {
            case JsonObject jsonObject:
            {
                if (jsonObject["$ref"] is JsonValue referenceValue &&
                    referenceValue.TryGetValue<string>(out var reference) &&
                    reference.StartsWith("#/", StringComparison.Ordinal))
                {
                    if (!references.Add(reference))
                    {
                        problems.Add($"schema: циклическая ссылка '{reference}' ({path})");

                        return null;
                    }

                    var target = ResolvePointer(root, reference);

                    if (target is null)
                    {
                        references.Remove(reference);
                        problems.Add($"ref: '{reference}' не найдена в документе ({path})");

                        return null;
                    }

                    var inlined = InlineReferences(
                        target.DeepClone(),
                        root,
                        depth + 1,
                        references,
                        problems,
                        $"{path}->{reference}");

                    references.Remove(reference);

                    return inlined;
                }

                var result = new JsonObject();

                foreach (var property in jsonObject)
                {
                    result[property.Key] = InlineReferences(
                        property.Value?.DeepClone(),
                        root,
                        depth,
                        references,
                        problems,
                        $"{path}.{property.Key}");
                }

                return result;
            }

            case JsonArray jsonArray:
            {
                var result = new JsonArray();

                foreach (var item in jsonArray)
                {
                    result.Add(InlineReferences(item?.DeepClone(), root, depth, references, problems, path));
                }

                return result;
            }

            default:
                return node.DeepClone();
        }
    }

    public static void Validate(JsonObject root, List<string> problems, List<string> warnings)
    {
        Walk(root, "#", root, problems, warnings);
        ValidatePaths(root, problems);
    }

    /// <summary>Извлечение операций из раздела <c>paths</c> исходного документа.</summary>
    public static IReadOnlyList<OpenApiOperationDefinition> ExtractOperations(JsonObject root)
    {
        var operations = new List<OpenApiOperationDefinition>();

        if (root["paths"] is not JsonObject paths)
        {
            return operations;
        }

        foreach (var path in paths)
        {
            if (path.Value is not JsonObject pathItem)
            {
                continue;
            }

            foreach (var member in pathItem)
            {
                if (!HttpMethods.Contains(member.Key))
                {
                    continue;
                }

                string? operationId = null;

                if (member.Value is JsonObject operation &&
                    operation["operationId"] is JsonValue operationIdValue &&
                    operationIdValue.TryGetValue<string>(out var parsed) &&
                    !string.IsNullOrWhiteSpace(parsed))
                {
                    operationId = parsed;
                }

                operations.Add(new OpenApiOperationDefinition(
                    member.Key.ToUpperInvariant(),
                    path.Key,
                    operationId));
            }
        }

        return operations;
    }

    private static void ValidatePaths(JsonObject root, List<string> problems)
    {
        if (root["paths"] is not JsonObject paths)
        {
            problems.Add("paths: раздел paths отсутствует");

            return;
        }

        foreach (var path in paths)
        {
            if (!path.Key.StartsWith('/'))
            {
                problems.Add($"path: шаблон '{path.Key}' должен начинаться с '/'");
            }
        }
    }

    private static void Walk(
        JsonNode? node,
        string path,
        JsonObject root,
        List<string> problems,
        List<string> warnings)
    {
        switch (node)
        {
            case JsonObject jsonObject:
                ValidateReference(jsonObject, path, root, problems);

                foreach (var property in jsonObject)
                {
                    var childPath = $"{path}.{property.Key}";

                    if (ForbiddenKeywords.Contains(property.Key))
                    {
                        problems.Add($"schema: конструкция '{property.Key}' не поддерживается ({childPath})");
                    }

                    if (string.Equals(property.Key, "format", StringComparison.Ordinal) &&
                        property.Value is JsonValue formatValue &&
                        formatValue.TryGetValue<string>(out var format) &&
                        !KnownFormats.Contains(format))
                    {
                        warnings.Add($"format: '{format}' не проверяется ({childPath})");
                    }

                    Walk(property.Value, childPath, root, problems, warnings);
                }

                break;

            case JsonArray jsonArray:
                for (var index = 0; index < jsonArray.Count; index++)
                {
                    Walk(jsonArray[index], $"{path}[{index}]", root, problems, warnings);
                }

                break;
        }
    }

    private static void ValidateReference(
        JsonObject jsonObject,
        string path,
        JsonObject root,
        List<string> problems)
    {
        if (!jsonObject.ContainsKey("$ref"))
        {
            return;
        }

        if (jsonObject["$ref"] is not JsonValue referenceValue ||
            !referenceValue.TryGetValue<string>(out var reference) ||
            string.IsNullOrWhiteSpace(reference))
        {
            problems.Add($"ref: пустая ссылка ({path})");

            return;
        }

        if (!reference.StartsWith("#/", StringComparison.Ordinal))
        {
            problems.Add($"ref: '{reference}' запрещена, разрешены только локальные ссылки ({path})");

            return;
        }

        if (ResolvePointer(root, reference) is null)
        {
            problems.Add($"ref: '{reference}' не найдена в документе ({path})");
        }

        var siblings = jsonObject
            .Select(property => property.Key)
            .Where(key => !string.Equals(key, "$ref", StringComparison.Ordinal) &&
                          !key.StartsWith("x-", StringComparison.Ordinal))
            .ToArray();

        if (siblings.Length > 0)
        {
            problems.Add(
                $"ref: поля рядом с $ref не поддерживаются ({string.Join(", ", siblings)}); " +
                $"используйте allOf или отдельную схему ({path})");
        }
    }

    private static JsonNode? ResolvePointer(JsonObject root, string pointer)
    {
        JsonNode? current = root;

        foreach (var rawSegment in pointer[2..].Split('/'))
        {
            var segment = rawSegment
                .Replace("~1", "/", StringComparison.Ordinal)
                .Replace("~0", "~", StringComparison.Ordinal);

            current = current switch
            {
                JsonObject jsonObject when jsonObject.TryGetPropertyValue(segment, out var child) => child,
                JsonArray jsonArray when int.TryParse(segment, out var index) &&
                                         index >= 0 &&
                                         index < jsonArray.Count => jsonArray[index],
                _ => null,
            };

            if (current is null)
            {
                return null;
            }
        }

        return current;
    }
}
