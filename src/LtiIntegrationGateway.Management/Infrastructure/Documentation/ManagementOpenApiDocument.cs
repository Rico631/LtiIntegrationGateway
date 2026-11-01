using System.Text.Json;
using System.Text.Json.Nodes;
using Google.Api;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using LtiIntegrationGateway.Contracts.Management.V1;
using FieldLabel = Google.Protobuf.Reflection.FieldDescriptorProto.Types.Label;
using FieldType = Google.Protobuf.Reflection.FieldDescriptorProto.Types.Type;

namespace LtiIntegrationGateway.Infrastructure.Documentation;

/// <summary>
/// OpenAPI 3.0-описание управляющего API, построенное из дескриптора gRPC-контракта: маршруты и
/// методы берутся из аннотаций <c>google.api.http</c>, схемы и описания — из сообщений и комментариев
/// <c>management_api.proto</c>. Второго источника истины не создаётся.
/// </summary>
public static class ManagementOpenApiDocument
{
    /// <summary>Имя встроенного дескриптора контракта; задаётся в проекте Contracts.</summary>
    private const string DescriptorResourceName = "management_api.desc";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>Разбор дескриптора с аннотациями <c>google.api.http</c>: без реестра расширений они не читаются.</summary>
    private static readonly MessageParser<FileDescriptorSet> ContractParser = CreateParser();

    /// <summary>OpenAPI-документ управляющего API в формате JSON.</summary>
    public static string CreateJson() => new Document(ReadContract()).ToJson().ToJsonString(WriteOptions);

    private static FileDescriptorProto ReadContract()
    {
        using var stream = typeof(ManagementService).Assembly.GetManifestResourceStream(DescriptorResourceName)
            ?? throw new InvalidOperationException(
                $"Дескриптор контракта '{DescriptorResourceName}' не встроен в сборку контрактов.");

        // Дескриптор собирается без импортов, поэтому в наборе ровно один файл.
        return ContractParser.ParseFrom(stream).File.Single();
    }

    private static MessageParser<FileDescriptorSet> CreateParser()
    {
        var registry = new ExtensionRegistry();
        registry.Add(AnnotationsExtensions.Http);

        return FileDescriptorSet.Parser.WithExtensionRegistry(registry);
    }

    /// <summary>Построение документа по одному .proto-файлу: пути, схемы, описания.</summary>
    private sealed class Document(FileDescriptorProto file)
    {
        private readonly Dictionary<string, (DescriptorProto Proto, string CommentKey)> messages =
            IndexMessages(file);

        private readonly Dictionary<string, (EnumDescriptorProto Proto, string CommentKey)> enums =
            IndexEnums(file);

        private readonly Dictionary<string, string> comments = ReadComments(file.SourceCodeInfo);
        private readonly JsonObject schemas = new();
        private readonly HashSet<string> described = new(StringComparer.Ordinal);

        private string PackagePrefix => "." + file.Package + ".";

        public JsonObject ToJson()
        {
            var paths = new JsonObject();

            for (var serviceIndex = 0; serviceIndex < file.Service.Count; serviceIndex++)
            {
                var service = file.Service[serviceIndex];

                for (var methodIndex = 0; methodIndex < service.Method.Count; methodIndex++)
                {
                    var method = service.Method[methodIndex];

                    if (HttpRuleOf(method) is not { } rule || HttpMethodOf(rule) is not { } httpMethod)
                    {
                        continue;
                    }

                    var template = TemplateOf(rule);

                    if (paths[template] is not JsonObject path)
                    {
                        path = new JsonObject();
                        paths[template] = path;
                    }

                    path[httpMethod] = Operation(serviceIndex, methodIndex, method, rule, template);
                }
            }

            return new JsonObject
            {
                ["openapi"] = "3.0.1",
                ["info"] = Info(),
                ["paths"] = paths,
                ["components"] = new JsonObject
                {
                    ["schemas"] = schemas,
                    ["securitySchemes"] = SecuritySchemes(),
                },
                ["security"] = new JsonArray(new JsonObject { ["managementApiKey"] = new JsonArray() }),
            };
        }

        private JsonObject Info() => new()
        {
            ["title"] = "Управляющий API LTI Integration Gateway",
            ["version"] = "v1",
            ["description"] = Comment(string.Empty) ??
                "Управляющие операции шлюза: gRPC и REST через JSON transcoding.",
        };

        private JsonObject Operation(
            int serviceIndex,
            int methodIndex,
            MethodDescriptorProto method,
            HttpRule rule,
            string template)
        {
            var operation = new JsonObject
            {
                ["operationId"] = method.Name,
                ["tags"] = new JsonArray(file.Service[serviceIndex].Name),
                ["parameters"] = Parameters(messages[LocalName(method.InputType)], PathVariables(template), rule),
            };

            if (Comment($"6.{serviceIndex}.2.{methodIndex}") is { } comment)
            {
                operation["summary"] = comment.Split('\n')[0];
                operation["description"] = comment;
            }

            if (rule.Body.Length > 0)
            {
                operation["requestBody"] = new JsonObject
                {
                    ["required"] = true,
                    // Частичное тело (body: "<field>") в контракте не используется: тело передаётся целиком.
                    ["content"] = JsonContent(Reference(method.InputType)),
                };
            }

            operation["responses"] = Responses(method.OutputType);

            return operation;
        }

        private JsonArray Parameters(
            (DescriptorProto Proto, string CommentKey) request,
            List<string> pathVariables,
            HttpRule rule)
        {
            var parameters = new JsonArray();

            for (var index = 0; index < request.Proto.Field.Count; index++)
            {
                var field = request.Proto.Field[index];
                var inPath = pathVariables.Contains(field.Name, StringComparer.Ordinal);

                // Тело передаётся целиком, поэтому остальные поля запроса параметрами не являются.
                if (!inPath && (rule.Body.Length > 0 || !IsScalar(field)))
                {
                    continue;
                }

                var parameter = new JsonObject
                {
                    ["name"] = inPath ? field.Name : field.JsonName,
                    ["in"] = inPath ? "path" : "query",
                    ["required"] = inPath,
                    ["schema"] = Schema(field),
                };

                if (Comment($"{request.CommentKey}.2.{index}") is { } description)
                {
                    parameter["description"] = description;
                }

                parameters.Add(parameter);
            }

            return parameters;
        }

        private JsonObject Responses(string outputType) => new()
        {
            ["200"] = new JsonObject
            {
                ["description"] =
                    "Результат операции. Доменный отказ — 200 с succeeded = false и полным списком problems; " +
                    "активная конфигурация при отказе не меняется.",
                ["content"] = JsonContent(Reference(outputType)),
            },
            ["401"] = new JsonObject { ["description"] = "Ключ API отсутствует или неверен." },
            ["503"] = new JsonObject { ["description"] = "Ключ API не настроен на сервере." },
        };

        /// <summary>Ссылка на схему сообщения или перечисления; внешние типы описываются скалярами.</summary>
        private JsonObject Reference(string typeName)
        {
            if (typeName == ".google.protobuf.Timestamp")
            {
                // ProtoJSON передаёт Timestamp строкой RFC 3339.
                return Scalar("string", "date-time");
            }

            if (!typeName.StartsWith(PackagePrefix, StringComparison.Ordinal))
            {
                return new JsonObject { ["type"] = "object" };
            }

            var name = LocalName(typeName);

            if (described.Add(name))
            {
                schemas[name] = Describe(name);
            }

            return new JsonObject { ["$ref"] = "#/components/schemas/" + name };
        }

        private JsonObject Describe(string name)
        {
            if (messages.TryGetValue(name, out var message))
            {
                var properties = new JsonObject();

                for (var index = 0; index < message.Proto.Field.Count; index++)
                {
                    var field = message.Proto.Field[index];
                    properties[field.JsonName] = Schema(field, Comment($"{message.CommentKey}.2.{index}"));
                }

                var schema = new JsonObject { ["type"] = "object", ["properties"] = properties };
                AddDescription(schema, Comment(message.CommentKey));

                return schema;
            }

            if (enums.TryGetValue(name, out var enumType))
            {
                var values = new JsonArray();

                foreach (var value in enumType.Proto.Value)
                {
                    values.Add(value.Name);
                }

                var schema = new JsonObject { ["type"] = "string", ["enum"] = values };
                AddDescription(schema, Comment(enumType.CommentKey));

                return schema;
            }

            return new JsonObject { ["type"] = "object" };
        }

        private JsonObject Schema(FieldDescriptorProto field, string? description = null)
        {
            var schema = field.Label == FieldLabel.Repeated
                ? new JsonObject { ["type"] = "array", ["items"] = ValueSchema(field) }
                : ValueSchema(field);

            if (string.IsNullOrWhiteSpace(description))
            {
                return schema;
            }

            if (schema["$ref"] is null)
            {
                schema["description"] = description;

                return schema;
            }

            // Рядом с $ref описание в OpenAPI 3.0 не читается, поэтому ссылка оборачивается в allOf.
            return new JsonObject { ["allOf"] = new JsonArray(schema), ["description"] = description };
        }

        private JsonObject ValueSchema(FieldDescriptorProto field) => field.Type switch
        {
            FieldType.String => Scalar("string"),
            FieldType.Bool => Scalar("boolean"),
            FieldType.Bytes => Scalar("string", "byte"),
            FieldType.Int32 or FieldType.Sint32 or FieldType.Sfixed32 => Scalar("integer", "int32"),
            FieldType.Uint32 or FieldType.Fixed32 => Scalar("integer", "int64"),
            // 64-битные целые ProtoJSON передаёт строкой.
            FieldType.Int64 or FieldType.Sint64 or FieldType.Sfixed64 => Scalar("string", "int64"),
            FieldType.Uint64 or FieldType.Fixed64 => Scalar("string", "uint64"),
            FieldType.Float => Scalar("number", "float"),
            FieldType.Double => Scalar("number", "double"),
            FieldType.Enum or FieldType.Message or FieldType.Group => Reference(field.TypeName),
            _ => new JsonObject { ["type"] = "object" },
        };

        private string LocalName(string typeName) => typeName[PackagePrefix.Length..];

        private string? Comment(string key) => comments.TryGetValue(key, out var comment) ? comment : null;

        private static bool IsScalar(FieldDescriptorProto field) =>
            field.Type is not (FieldType.Message or FieldType.Group);

        private static Dictionary<string, (DescriptorProto Proto, string CommentKey)> IndexMessages(
            FileDescriptorProto file)
        {
            var result = new Dictionary<string, (DescriptorProto Proto, string CommentKey)>(StringComparer.Ordinal);

            for (var index = 0; index < file.MessageType.Count; index++)
            {
                result[file.MessageType[index].Name] = (file.MessageType[index], $"4.{index}");
            }

            return result;
        }

        private static Dictionary<string, (EnumDescriptorProto Proto, string CommentKey)> IndexEnums(
            FileDescriptorProto file)
        {
            var result = new Dictionary<string, (EnumDescriptorProto Proto, string CommentKey)>(StringComparer.Ordinal);

            for (var index = 0; index < file.EnumType.Count; index++)
            {
                result[file.EnumType[index].Name] = (file.EnumType[index], $"5.{index}");
            }

            return result;
        }

        /// <summary>
        /// Комментарии исходного файла по пути объявления, например <c>4.0.2.1</c>. Шапка файла
        /// отделена от <c>syntax</c> пустой строкой, поэтому protoc отдаёт её detached-комментарием
        /// первой локации; она попадает в описание документа под пустым ключом.
        /// </summary>
        private static Dictionary<string, string> ReadComments(SourceCodeInfo? sourceInfo)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);

            if (sourceInfo is null)
            {
                return result;
            }

            foreach (var location in sourceInfo.Location)
            {
                if (location.LeadingDetachedComments.Count > 0 && !result.ContainsKey(string.Empty))
                {
                    result[string.Empty] = string.Join('\n', location.LeadingDetachedComments.Select(Normalize));
                }

                if (!string.IsNullOrWhiteSpace(location.LeadingComments))
                {
                    result[string.Join('.', location.Path)] = Normalize(location.LeadingComments);
                }
            }

            return result;
        }

        private static string Normalize(string comment) =>
            string.Join(
                '\n',
                comment
                    .Split('\n')
                    .Select(line => line.StartsWith(' ') ? line[1..].TrimEnd() : line.TrimEnd()))
                .Trim();

        private static HttpRule? HttpRuleOf(MethodDescriptorProto method) =>
            method.Options is { } options && options.HasExtension(AnnotationsExtensions.Http)
                ? options.GetExtension(AnnotationsExtensions.Http)
                : null;

        private static string? HttpMethodOf(HttpRule rule) => rule.PatternCase switch
        {
            HttpRule.PatternOneofCase.Get => "get",
            HttpRule.PatternOneofCase.Post => "post",
            HttpRule.PatternOneofCase.Put => "put",
            HttpRule.PatternOneofCase.Patch => "patch",
            HttpRule.PatternOneofCase.Delete => "delete",
            _ => null,
        };

        private static string TemplateOf(HttpRule rule) => rule.PatternCase switch
        {
            HttpRule.PatternOneofCase.Get => rule.Get,
            HttpRule.PatternOneofCase.Post => rule.Post,
            HttpRule.PatternOneofCase.Put => rule.Put,
            HttpRule.PatternOneofCase.Patch => rule.Patch,
            HttpRule.PatternOneofCase.Delete => rule.Delete,
            _ => string.Empty,
        };

        /// <summary>Переменные шаблона пути: сегменты вида <c>{channel_name}</c>.</summary>
        private static List<string> PathVariables(string template) =>
            [.. template.Split('/')
                .Where(segment => segment.StartsWith('{') && segment.EndsWith('}'))
                .Select(segment => segment[1..^1])];

        private static JsonObject SecuritySchemes() => new()
        {
            ["managementApiKey"] = new JsonObject
            {
                ["type"] = "apiKey",
                ["in"] = "header",
                ["name"] = "X-Management-Api-Key",
                ["description"] = "Ключ API управляющего API; имя заголовка настраивается Management:HeaderName.",
            },
        };

        private static void AddDescription(JsonObject schema, string? description)
        {
            if (description is not null)
            {
                schema["description"] = description;
            }
        }

        private static JsonObject Scalar(string type, string? format = null) => format is null
            ? new JsonObject { ["type"] = type }
            : new JsonObject { ["type"] = type, ["format"] = format };

        private static JsonObject JsonContent(JsonObject schema) =>
            new() { ["application/json"] = new JsonObject { ["schema"] = schema } };
    }
}
