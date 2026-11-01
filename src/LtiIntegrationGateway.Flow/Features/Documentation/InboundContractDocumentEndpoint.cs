using System.Text.Json;
using System.Text.Json.Nodes;
using LtiIntegrationGateway.Features.Configuration;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LtiIntegrationGateway.Features.Documentation;

/// <summary>
/// Сводный OpenAPI входящих контрактов интеграции: что опубликовано и доступно для вызовов.
/// Документ только для ознакомления — шлюз отвечает по активным версиям контрактов из хранилища.
/// </summary>
public static class InboundContractDocumentEndpoint
{
    /// <summary>Публикует <c>/openapi/inbound.json</c>; Swagger UI показывает его без собственных endpoint'ов.</summary>
    public static IEndpointRouteBuilder MapInboundContractDocument(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/openapi/inbound.json", GetAsync);

        return endpoints;
    }

    private static async Task<IResult> GetAsync(
        GatewayDbContext db,
        IOptions<FlowOptions> flowOptions,
        CancellationToken cancellationToken)
    {
        var integration = flowOptions.Value.Integration;

        var contracts = await db.ContractVersions
            .Where(version => version.Status == ContractVersionStatus.Active &&
                              version.InboundChannel!.IntegrationId == integration)
            .OrderBy(version => version.InboundChannel!.Name)
            .Select(version => new { version.InboundChannel!.Name, version.Document })
            .ToListAsync(cancellationToken);

        var paths = new JsonObject();
        var schemas = new JsonObject();
        var tags = new JsonArray();

        foreach (var contract in contracts)
        {
            tags.Add(new JsonObject { ["name"] = contract.Name });

            InboundContractDocument.Merge(contract.Name, contract.Document, paths, schemas);
        }

        var document = new JsonObject
        {
            ["openapi"] = "3.0.3",
            ["info"] = new JsonObject
            {
                ["title"] = $"Входящие контракты интеграции {integration}",
                ["version"] = "1",
            },
            ["tags"] = tags,
            ["paths"] = paths,
        };

        if (schemas.Count > 0)
        {
            document["components"] = new JsonObject { ["schemas"] = schemas };
        }

        return Results.Text(
            document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            "application/json");
    }
}
