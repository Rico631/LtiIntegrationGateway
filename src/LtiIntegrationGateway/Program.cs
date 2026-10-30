using LtiIntegrationGateway.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;

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

var app = builder.Build();

// Конфигурация окончательно собирается к этому моменту, поэтому проверка fail-fast
// не зависит от порядка применения источников и переопределений в тестах.
if (string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString("Gateway")))
{
    throw new InvalidOperationException(MissingConnectionStringMessage);
}

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });

app.Run();

public partial class Program { }


