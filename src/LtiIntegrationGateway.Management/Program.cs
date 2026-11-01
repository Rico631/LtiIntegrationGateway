using LtiIntegrationGateway.Features.Channels;
using LtiIntegrationGateway.Features.Channels.ActivateChannelRevision;
using LtiIntegrationGateway.Features.Channels.CreateChannel;
using LtiIntegrationGateway.Features.Channels.GetChannel;
using LtiIntegrationGateway.Features.Channels.GetChannelHealth;
using LtiIntegrationGateway.Features.Channels.PublishChannelRevision;
using LtiIntegrationGateway.Features.Channels.RollbackChannelRevision;
using LtiIntegrationGateway.Features.Channels.UpdateChannelDraft;
using LtiIntegrationGateway.Features.Contracts.ActivateContractVersion;
using LtiIntegrationGateway.Features.Contracts.GetContractVersion;
using LtiIntegrationGateway.Features.Contracts.ListContractOperations;
using LtiIntegrationGateway.Features.Contracts.PublishContractVersion;
using LtiIntegrationGateway.Features.Contracts.UpdateContractOperationSettings;
using LtiIntegrationGateway.Features.Contracts.UpdateContractVersionSettings;
using LtiIntegrationGateway.Features.Contracts.UploadContractVersion;
using LtiIntegrationGateway.Features.Host;
using LtiIntegrationGateway.Features.InboundChannels.CreateInboundChannel;
using LtiIntegrationGateway.Features.InboundChannels.UpdateInboundChannelSettings;
using LtiIntegrationGateway.Features.Management;
using LtiIntegrationGateway.Features.ProductRoutes;
using LtiIntegrationGateway.Features.ProductRoutes.GetProductRoute;
using LtiIntegrationGateway.Infrastructure.Documentation;
using LtiIntegrationGateway.Infrastructure.Health;
using LtiIntegrationGateway.Infrastructure.Management;
using LtiIntegrationGateway.Infrastructure.Observability;
using LtiIntegrationGateway.Infrastructure.OpenApi;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Routing;
using LtiIntegrationGateway.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGatewayStorage(builder.Configuration);

builder.Services.AddGatewayObservability(builder.Configuration, builder.Environment, "management");
builder.Services.AddSingleton<DatabaseHealthCheck>();
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("storage", tags: ["ready"]);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ISecretStore, ConfigurationSecretStore>();
builder.Services.AddSingleton<IChannelCertificateStore, ConfigurationChannelCertificateStore>();
builder.Services.AddSingleton<IOpenApiDocumentReader, MicrosoftOpenApiDocumentReader>();
builder.Services.AddSingleton<InboundRouteSetValidator>();

builder.Services.AddScoped<ChannelConfigurationValidator>();
builder.Services.AddScoped<StartupSecretValidator>();
builder.Services.AddScoped<CreateChannelHandler>();
builder.Services.AddScoped<CreateInboundChannelHandler>();
builder.Services.AddScoped<UpdateInboundChannelSettingsHandler>();
builder.Services.AddScoped<UpdateChannelDraftHandler>();
builder.Services.AddScoped<PublishChannelRevisionHandler>();
builder.Services.AddScoped<ActivateChannelRevisionHandler>();
builder.Services.AddScoped<RollbackChannelRevisionHandler>();
builder.Services.AddScoped<GetChannelHandler>();
builder.Services.AddScoped<GetChannelHealthHandler>();
builder.Services.AddScoped<RegisterProductRouteHandler>();
builder.Services.AddScoped<GetProductRouteHandler>();
builder.Services.AddScoped<ManagementAuditWriter>();
builder.Services.AddScoped<UploadContractVersionHandler>();
builder.Services.AddScoped<PublishContractVersionHandler>();
builder.Services.AddScoped<ActivateContractVersionHandler>();
builder.Services.AddScoped<ListContractOperationsHandler>();
builder.Services.AddScoped<GetContractVersionHandler>();
builder.Services.AddScoped<UpdateContractVersionSettingsHandler>();
builder.Services.AddScoped<UpdateContractOperationSettingsHandler>();
builder.Services.AddScoped<ManagementApiService>();

builder.Services.Configure<ManagementApiKeyOptions>(
    builder.Configuration.GetSection(ManagementApiKeyOptions.SectionName));

builder.Services.AddGrpc().AddJsonTranscoding();

var app = builder.Build();

// Конфигурация окончательно собирается к этому моменту, поэтому проверка fail-fast
// не зависит от порядка применения источников и переопределений в тестах.
if (string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString(GatewayStorageExtensions.ConnectionStringName)))
{
    throw new InvalidOperationException(GatewayStorageExtensions.MissingConnectionStringMessage);
}

// Схемой владеет только сервис управления.
using (var migrationScope = app.Services.CreateScope())
{
    try
    {
        migrationScope.ServiceProvider.GetRequiredService<GatewayDbContext>().Database.Migrate();
    }
    catch (Exception exception) when (GatewayStorageExtensions.IsAuthenticationFailure(exception))
    {
        // Причина отказа аутентификации не раскрывает значение пароля и указывает способ исправления.
        throw new InvalidOperationException(
            GatewayStorageExtensions.AuthenticationFailureMessage,
            exception);
    }
}

using (var startupScope = app.Services.CreateScope())
{
    var missingSecrets = await startupScope.ServiceProvider
        .GetRequiredService<StartupSecretValidator>()
        .FindMissingAsync();

    if (missingSecrets.Count > 0)
    {
        throw new InvalidOperationException(
            "Не настроены секреты активной конфигурации: " + string.Join("; ", missingSecrets));
    }
}

app.UseMiddleware<ManagementApiKeyMiddleware>();

// OpenAPI-описание строится из того же дескриптора контракта, что и transcoding-маршруты,
// поэтому расхождение документа с gRPC-контрактом невозможно.
var managementOpenApi = ManagementOpenApiDocument.CreateJson();

app.MapGet("/openapi/management.json", () => Results.Text(managementOpenApi, "application/json"));
app.MapGrpcService<ManagementApiService>();
app.MapGatewayHealthProbes();

app.UseSwaggerUI(options =>
{
    options.RoutePrefix = "swagger";
    options.SwaggerEndpoint("/openapi/management.json", "Управляющий API v1");
});

app.Run();

public partial class Program { }
