var builder = DistributedApplication.CreateBuilder(args);

const string DevManagementApiKey = "dev-management-api-key";

// Пароль фиксирован: Aspire иначе генерирует новый при каждом запуске, тогда как том PostgreSQL
// сохраняет пароль первого запуска — подключение падает с 28P01.
var postgresPassword = builder.AddParameter(
    "postgres-password",
    "dev-postgres-password",
    secret: true);

var postgres = builder.AddPostgres("postgres")
    .WithPassword(postgresPassword)
    .WithDataVolume("lti-gateway-postgres-data")
    .AddDatabase("gateway");

// Единый сервис управления конфигурацией: две реплики, схемой владеет только он.
builder.AddProject<Projects.LtiIntegrationGateway_Management>("management")
    .WithHttpEndpoint()
    .WithHttpHealthCheck("/health/ready")
    .WithReference(postgres)
    .WithReplicas(2)
    .WithEnvironment("Observability__Otel__Enabled", "true")
    .WithEnvironment("Management__ApiKey", DevManagementApiKey);

// По экземпляру потоков на интеграцию; реплики одной интеграции делят её очередь.
AddFlow(builder, postgres, "bank-a");
AddFlow(builder, postgres, "bank-b");

builder.Build().Run();

static void AddFlow(
    IDistributedApplicationBuilder builder,
    IResourceBuilder<PostgresDatabaseResource> database,
    string integration)
{
    builder.AddProject<Projects.LtiIntegrationGateway_Flow>($"flow-{integration}")
        .WithHttpEndpoint()
        // Dashboard показывает состояние готовности: проба включает проверку хранилища.
        .WithHttpHealthCheck("/health/ready")
        .WithReference(database)
        .WithReplicas(2)
        .WithEnvironment("Gateway__Integration", integration)
        .WithEnvironment("Observability__Otel__Enabled", "true");
}
