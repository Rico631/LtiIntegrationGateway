using LtiIntegrationGateway.Infrastructure.Health;
using LtiIntegrationGateway.Infrastructure.Observability;
using LtiIntegrationGateway.Infrastructure.Rebus;
using LtiIntegrationGateway.Messaging;
using LtiIntegrationGateway.Simulation.Product;
using LtiIntegrationGateway.Simulation.Product.Handlers;
using Rebus.Config;

var builder = WebApplication.CreateBuilder(args);

var options = ProductSimulationOptions.FromConfiguration(builder.Configuration);

builder.Services.AddSingleton(options);
builder.Services.AddGatewayObservability(builder.Configuration, builder.Environment, "product");
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

// Клиент управляющего API: ключ и адрес задаются конфигурацией, а не кодом.
builder.Services.AddHttpClient<ManagementClient>((provider, client) =>
{
    var settings = provider.GetRequiredService<ProductSimulationOptions>();

    client.BaseAddress = new Uri(settings.ManagementBaseUrl);
    client.DefaultRequestHeaders.Add(settings.ManagementHeaderName, settings.ManagementApiKey);
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddRebus((configure, provider) =>
{
    var connectionString = provider.GetRequiredService<IConfiguration>().GetConnectionString("Gateway")
        ?? throw new InvalidOperationException("Строка подключения 'ConnectionStrings:Gateway' не настроена.");

    return configure
        .Transport(transport => transport.UsePostgreSql(
            connectionString,
            tableName: QueueNames.MessageTable,
            inputQueueName: QueueNames.ProductInbox(options.ProductId)))
        .Serialization(serialization => serialization.Register(
            _ => new ProtobufSerializer(new ProtobufMessageRegistry())));
});

builder.Services.AutoRegisterHandlersFromAssemblyOf<OutboundResultHandler>();

// Сначала конфигурация в управляющем API, затем отправка событий.
builder.Services.AddHostedService<ManagementBootstrapHostedService>();
builder.Services.AddHostedService<OutboundLoopService>();

var app = builder.Build();

// Сервис существует только в dev-контуре, поэтому описание доступно всегда.
app.MapOpenApi();

// Готовность означает, что конфигурация зарегистрирована: до этого Kestrel не начинает слушать.
app.MapGatewayHealthProbes();

// Контракты inbound-каналов: те же документы, что загружены в управляющий API.
app.MapGet("/contracts/{name}", (string name) => Contracts.TryRead(name) is { } document
        ? Results.Text(document, "application/json")
        : Results.NotFound())
    .WithSummary("Отдаёт OpenAPI-контракт продукта, зарегистрированный во входящем канале.");

app.UseSwaggerUI(ui =>
{
    ui.RoutePrefix = "swagger";
    ui.SwaggerEndpoint("/openapi/v1.json", "Симуляция: продукт");
    ui.SwaggerEndpoint($"/contracts/{Contracts.AsyncOrders}", "Контракт: POST /order (async)");
    ui.SwaggerEndpoint($"/contracts/{Contracts.SyncOrders}", "Контракт: GET /order/{orderid} (sync)");
});

app.Run();

