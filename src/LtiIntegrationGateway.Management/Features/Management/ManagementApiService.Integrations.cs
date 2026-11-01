using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using LtiIntegrationGateway.Contracts.Management.V1;
using Microsoft.EntityFrameworkCore;

namespace LtiIntegrationGateway.Features.Management;

/// <summary>Перечисление интеграций, для которых существует конфигурация.</summary>
public sealed partial class ManagementApiService
{
    public override async Task<ListIntegrationsResponse> ListIntegrations(
        ListIntegrationsRequest request,
        ServerCallContext context)
    {
        var integrations = await db.Integrations
            .AsNoTracking()
            .OrderBy(integration => integration.IntegrationId)
            .ToListAsync(context.CancellationToken);

        var response = new ListIntegrationsResponse();

        response.Integrations.AddRange(integrations.Select(integration => new IntegrationInfo
        {
            Integration = integration.IntegrationId,
            ConfigurationVersion = integration.ConfigurationVersion,
            CreatedAt = Timestamp.FromDateTimeOffset(integration.CreatedAt),
        }));

        return response;
    }
}
