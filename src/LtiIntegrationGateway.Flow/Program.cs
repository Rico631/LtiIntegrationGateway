using System.Threading.RateLimiting;
using LtiIntegrationGateway.Contracts.Messages.V1;
using LtiIntegrationGateway.Features.Configuration;
using LtiIntegrationGateway.Features.Documentation;
using LtiIntegrationGateway.Features.Host;
using LtiIntegrationGateway.Features.Inbound;
using LtiIntegrationGateway.Features.Outbound;
using LtiIntegrationGateway.Infrastructure.Health;
using LtiIntegrationGateway.Infrastructure.Http;
using LtiIntegrationGateway.Infrastructure.Management;
using LtiIntegrationGateway.Infrastructure.Observability;
using LtiIntegrationGateway.Infrastructure.OpenApi;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Rebus;
using LtiIntegrationGateway.Infrastructure.Routing;
using LtiIntegrationGateway.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Rebus.Handlers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGatewayStorage(builder.Configuration);

builder.Services.AddGatewayObservability(builder.Configuration, builder.Environment, "flow");
builder.Services.AddSingleton<DatabaseHealthCheck>();
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("storage", tags: ["ready"]);

builder.Services.Configure<FlowOptions>(builder.Configuration.GetSection(FlowOptions.SectionName));
builder.Services.AddGatewayRebus();
builder.Services.AddSingleton<PendingInboundRequests>();
builder.Services.AddHostedService<FlowReplyBusHost>();
builder.Services.AddHostedService<OutboundBacklogMetricsUpdater>();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<StartupSecretValidator>();
builder.Services.AddScoped<ManagementAuditWriter>();
builder.Services.AddSingleton<ISecretStore, ConfigurationSecretStore>();
builder.Services.AddSingleton<IChannelCertificateStore, ConfigurationChannelCertificateStore>();
builder.Services.AddSingleton<ChannelHttpClientPool>();
builder.Services.AddSingleton<IChannelHttpClientFactory>(
    services => services.GetRequiredService<ChannelHttpClientPool>());

builder.Services.AddScoped<ResolveSenderIdentityHandler>();
builder.Services.AddScoped<LoadActiveChannelHandler>();
builder.Services.AddScoped<ResolveAuthorizationHandler>();
builder.Services.AddScoped<OutboundOperationRecorder>();
builder.Services.AddSingleton<StoredOutboundResultFactory>();
builder.Services.AddScoped<PublishOutboundResultHandler>();
builder.Services.AddScoped<ProcessOutboundRequestHandler>();
builder.Services.AddScoped<OutboundRequestHandler>();
builder.Services.AddScoped<IHandleMessages<OutboundRequest>>(services => services.GetRequiredService<OutboundRequestHandler>());
builder.Services.AddScoped<ExecuteHttpCallHandler>();

builder.Services.Configure<OutboundDeliveryOptions>(
    builder.Configuration.GetSection(OutboundDeliveryOptions.SectionName));
builder.Services.AddHostedService<OutboundResultDispatcher>();

builder.Services.AddSingleton<FlowConfigurationApplier>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<FlowConfigurationApplier>());

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

var app = builder.Build();

if (string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString(GatewayStorageExtensions.ConnectionStringName)))
{
    throw new InvalidOperationException(GatewayStorageExtensions.MissingConnectionStringMessage);
}

// Экземпляр обязан знать свою интеграцию: без неё неизвестны входная очередь, error-queue и область
// действия проверок. Ошибка возникает до начала обслуживания запросов.
var flowOptions = app.Services.GetRequiredService<IOptions<FlowOptions>>().Value;

flowOptions.Validate();
GatewayTelemetry.ConfigureIntegration(flowOptions.Integration);

// Миграции применяет сервис управления: здесь только проверка обеспеченности секретами
// конфигурации своей интеграции.
try
{
    using var startupScope = app.Services.CreateScope();

    var missingSecrets = await startupScope.ServiceProvider
        .GetRequiredService<StartupSecretValidator>()
        .FindMissingAsync(flowOptions.Integration);

    if (missingSecrets.Count > 0)
    {
        throw new InvalidOperationException(
            "Не настроены секреты активной конфигурации: " + string.Join("; ", missingSecrets));
    }
}
catch (Exception exception) when (GatewayStorageExtensions.IsAuthenticationFailure(exception))
{
    throw new InvalidOperationException(
        GatewayStorageExtensions.AuthenticationFailureMessage,
        exception);
}

app.UseRateLimiter();

// Проверено спайком: регистрация EndpointDataSource только в DI не подхватывается minimal hosting,
// поэтому источник добавляется в набор источников маршрутизации приложения напрямую.
var endpointRouteBuilder = (IEndpointRouteBuilder)app;
endpointRouteBuilder.DataSources.Add(app.Services.GetRequiredService<InboundEndpointDataSource>());

app.MapGatewayHealthProbes();

// Входящие контракты для ознакомления: документ собирается из активных версий интеграции.
app.MapInboundContractDocument();
app.UseSwaggerUI(ui =>
{
    ui.RoutePrefix = "swagger";
    ui.SwaggerEndpoint("/openapi/inbound.json", "Входящие контракты");
});

app.Run();

public partial class Program { }
