using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Contracts;
using NJsonSchema;

namespace LtiIntegrationGateway.Infrastructure.OpenApi;

/// <summary>Скомпилированные схемы тела одной операции: Content-Type → схема.</summary>
public sealed record InboundOperationSchema(IReadOnlyDictionary<string, JsonSchema> ContentSchemas)
{
    public bool HasBody => ContentSchemas.Count > 0;
}

/// <summary>
/// Кэш скомпилированных схем активных ревизий контрактов. Схемы собираются один раз при активации
/// версии и удаляются при её деактивации (ADR §14).
/// </summary>
public sealed class InboundSchemaCache
{
    private readonly ConcurrentDictionary<(Guid VersionId, string Method, string Template), InboundOperationSchema> _schemas = new();

    public InboundOperationSchema? Find(Guid versionId, string httpMethod, string template) =>
        _schemas.GetValueOrDefault((versionId, httpMethod, template));

    /// <summary>Компилирует схемы всех операций версии; вызывается при активации ревизии.</summary>
    public async Task WarmUpAsync(
        Guid versionId,
        IEnumerable<ContractOperation> operations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operations);

        foreach (var operation in operations)
        {
            if (string.IsNullOrWhiteSpace(operation.RequestSchemas))
            {
                continue;
            }

            if (JsonNode.Parse(operation.RequestSchemas) is not JsonObject content)
            {
                continue;
            }

            var compiled = new Dictionary<string, JsonSchema>(StringComparer.OrdinalIgnoreCase);

            foreach (var contentType in content)
            {
                if (contentType.Value is null)
                {
                    continue;
                }

                compiled[contentType.Key] = await JsonSchema.FromJsonAsync(
                    contentType.Value.ToJsonString(),
                    cancellationToken);
            }

            if (compiled.Count > 0)
            {
                _schemas[(versionId, operation.HttpMethod, operation.Template)] =
                    new InboundOperationSchema(compiled);
            }
        }
    }

    /// <summary>Удаляет схемы версии при её деактивации.</summary>
    public void Remove(Guid versionId)
    {
        foreach (var key in _schemas.Keys.Where(candidate => candidate.VersionId == versionId).ToArray())
        {
            _schemas.TryRemove(key, out _);
        }
    }
}
