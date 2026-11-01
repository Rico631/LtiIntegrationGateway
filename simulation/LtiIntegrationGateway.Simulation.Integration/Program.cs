using System.Text.Json;
using LtiIntegrationGateway.Infrastructure.Observability;
using LtiIntegrationGateway.Simulation.Integration;

var builder = WebApplication.CreateBuilder(args);

var options = IntegrationOptions.FromConfiguration(builder.Configuration);

builder.Services.AddSingleton(options);
builder.Services.AddGatewayObservability(builder.Configuration, builder.Environment, "integration");
builder.Services.AddOpenApi();
builder.Services.AddHttpClient();
//builder.Services.AddHostedService<IntegrationLoopService>();

var app = builder.Build();

// Сервис существует только в dev-контуре, поэтому описание доступно всегда.
app.MapOpenApi();

// Исходящий контур: шлюз вызывает этот endpoint и получает ответ внешней системы.
app.MapPost("/customer", (Customer customer, ILoggerFactory loggerFactory) =>
{
    var logger = loggerFactory.CreateLogger("Integration.Customer");

    logger.LogInformation("Получен customer от шлюза: {Body}", JsonSerializer.Serialize(customer));

    var response = new CustomerResponse("ok", $"recieved {customer.Name}");

    logger.LogInformation("Ответ шлюзу на POST /customer: {Body}", JsonSerializer.Serialize(response));

    return Results.Json(response);
}).WithSummary("Принимает customer от шлюза и возвращает подтверждение приёма.");

app.UseSwaggerUI(ui =>
{
    ui.RoutePrefix = "swagger";
    ui.SwaggerEndpoint("/openapi/v1.json", "Симуляция: внешняя система");
});

app.Run();

