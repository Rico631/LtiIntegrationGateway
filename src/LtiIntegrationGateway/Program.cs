using LtiIntegrationGateway.Contracts.Messages.V1;
using LtiIntegrationGateway.Features.Channels;
using LtiIntegrationGateway.Features.Outbound;
using LtiIntegrationGateway.Features.ProductRoutes;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Rebus;
using LtiIntegrationGateway.Infrastructure.Security;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
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
builder.Services.AddScoped<CreateChannelHandler>();
builder.Services.AddScoped<UpdateChannelDraftHandler>();
builder.Services.AddScoped<PublishChannelRevisionHandler>();
builder.Services.AddScoped<ActivateChannelRevisionHandler>();
builder.Services.AddScoped<RollbackChannelRevisionHandler>();
builder.Services.AddScoped<GetChannelHandler>();

builder.Services.AddSingleton<ISecretStore, ConfigurationSecretStore>();
builder.Services.AddScoped<RegisterProductRouteHandler>();
builder.Services.AddScoped<GetProductRouteHandler>();
builder.Services.AddScoped<ResolveSenderIdentityHandler>();
builder.Services.AddScoped<LoadActiveChannelHandler>();
builder.Services.AddScoped<ResolveAuthorizationHandler>();
builder.Services.AddScoped<OutboundOperationRecorder>();
builder.Services.AddScoped<PublishOutboundResultHandler>();
builder.Services.AddScoped<ProcessOutboundRequestHandler>();
builder.Services.AddScoped<OutboundRequestHandler>();
builder.Services.AddScoped<IHandleMessages<OutboundRequest>>(services => services.GetRequiredService<OutboundRequestHandler>());

builder.Services
    .AddHttpClient(ExecuteHttpCallHandler.ClientName)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

builder.Services.AddScoped<ExecuteHttpCallHandler>();

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

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });

app.Run();

public partial class Program { }


