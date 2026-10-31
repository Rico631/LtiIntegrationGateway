using System.Threading.RateLimiting;
using LtiIntegrationGateway.Contracts.Messages.V1;
using LtiIntegrationGateway.Features.Channels;
using LtiIntegrationGateway.Features.Contracts;
using LtiIntegrationGateway.Features.Host;
using LtiIntegrationGateway.Features.Inbound;
using LtiIntegrationGateway.Features.Management;
using LtiIntegrationGateway.Features.Outbound;
using LtiIntegrationGateway.Features.ProductRoutes;
using LtiIntegrationGateway.Infrastructure.Http;
using LtiIntegrationGateway.Infrastructure.Management;
using LtiIntegrationGateway.Infrastructure.OpenApi;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Rebus;
using LtiIntegrationGateway.Infrastructure.Routing;
using LtiIntegrationGateway.Infrastructure.Security;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Rebus.Handlers;

const string MissingConnectionStringMessage =
    "Строка подключения к хранилищу не настроена. " +
    "Задайте ключ конфигурации 'ConnectionStrings:Gateway'.";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<GatewayDbContext>((services, options) =>
{
    var configuration = services.GetRequiredService<IConfiguration>();

    var registeredConnectionString = configuration.GetConnectionString("Gateway")
        ?? throw new InvalidOperationException(MissingConnectionStringMessage);

    options.UseSqlite(registeredConnectionString);
});

builder.Services.AddHealthChecks();
builder.Services.AddGatewayRebus();

builder.Services.AddScoped<ChannelConfigurationValidator>();
builder.Services.AddScoped<StartupSecretValidator>();
builder.Services.AddScoped<CreateChannelHandler>();
builder.Services.AddScoped<UpdateChannelDraftHandler>();
builder.Services.AddScoped<PublishChannelRevisionHandler>();
builder.Services.AddScoped<ActivateChannelRevisionHandler>();
builder.Services.AddScoped<RollbackChannelRevisionHandler>();
builder.Services.AddScoped<GetChannelHandler>();
builder.Services.AddScoped<GetChannelHealthHandler>();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ISecretStore, ConfigurationSecretStore>();
builder.Services.AddSingleton<IChannelCertificateStore, ConfigurationChannelCertificateStore>();
builder.Services.AddSingleton<ChannelHttpClientPool>();
builder.Services.AddSingleton<IChannelHttpClientFactory>(
    services => services.GetRequiredService<ChannelHttpClientPool>());
builder.Services.AddScoped<RegisterProductRouteHandler>();
builder.Services.AddScoped<GetProductRouteHandler>();
builder.Services.AddScoped<ResolveSenderIdentityHandler>();
builder.Services.AddScoped<LoadActiveChannelHandler>();
builder.Services.AddScoped<ResolveAuthorizationHandler>();
builder.Services.AddScoped<OutboundOperationRecorder>();
builder.Services.AddSingleton<StoredOutboundResultFactory>();
builder.Services.AddScoped<PublishOutboundResultHandler>();
builder.Services.AddScoped<ProcessOutboundRequestHandler>();
builder.Services.AddScoped<OutboundRequestHandler>();
builder.Services.AddScoped<IHandleMessages<OutboundRequest>>(services => services.GetRequiredService<OutboundRequestHandler>());

builder.Services.Configure<OutboundDeliveryOptions>(
    builder.Configuration.GetSection(OutboundDeliveryOptions.SectionName));
builder.Services.AddHostedService<OutboundResultDispatcher>();

builder.Services.AddScoped<ExecuteHttpCallHandler>();

builder.Services.Configure<InboundRateLimitOptions>(
    builder.Configuration.GetSection(InboundRateLimitOptions.SectionName));

builder.Services.AddSingleton<InboundRouteCatalog>();
builder.Services.AddSingleton<InboundEndpointDataSource>();
builder.Services.AddSingleton<InboundRouteSetValidator>();
builder.Services.AddSingleton<InboundSchemaCache>();
builder.Services.AddSingleton<IInboundMessagePublisher, RebusInboundMessagePublisher>();
builder.Services.AddSingleton<InboundRequestAuthenticator>();
builder.Services.AddSingleton<InboundMessageLogWriter>();
builder.Services.AddSingleton<InboundRequestHandler>();
builder.Services.AddSingleton<InboundDisabledOperationHandler>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy(InboundRateLimit.PolicyName, httpContext =>
    {
        var defaults = httpContext.RequestServices
            .GetRequiredService<IOptions<InboundRateLimitOptions>>()
            .Value;

        var route = httpContext.GetEndpoint()?.Metadata.GetMetadata<InboundRouteMetadata>();

        var channel = route?.ChannelName
            ?? httpContext.GetEndpoint()?.Metadata.GetMetadata<InboundChannelMetadata>()?.ChannelName
            ?? "unknown";

        var permitLimit = route?.RateLimitPermit ?? defaults.PermitLimit;
        var windowSeconds = route?.RateLimitWindowSeconds ?? defaults.WindowSeconds;

        var partitionKey = $"{channel}|{permitLimit}|{windowSeconds}";

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromSeconds(windowSeconds),
            QueueLimit = 0,
        });
    });
});

builder.Services.AddSingleton<IOpenApiDocumentReader, MicrosoftOpenApiDocumentReader>();
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
if (string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString("Gateway")))
{
    throw new InvalidOperationException(MissingConnectionStringMessage);
}

using (var migrationScope = app.Services.CreateScope())
{
    migrationScope.ServiceProvider.GetRequiredService<GatewayDbContext>().Database.Migrate();
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

app.UseRateLimiter();

app.UseMiddleware<ManagementApiKeyMiddleware>();

// Проверено спайком: регистрация EndpointDataSource только в DI не подхватывается minimal hosting,
// поэтому источник добавляется в набор источников маршрутизации приложения напрямую.
var endpointRouteBuilder = (IEndpointRouteBuilder)app;
endpointRouteBuilder.DataSources.Add(app.Services.GetRequiredService<InboundEndpointDataSource>());

app.MapGrpcService<ManagementApiService>();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });

app.Run();

public partial class Program { }


