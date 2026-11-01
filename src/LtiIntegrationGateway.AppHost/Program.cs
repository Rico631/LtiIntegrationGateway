var builder = DistributedApplication.CreateBuilder(args);

const string DevManagementApiKey = "dev-management-api-key";

// Вся симуляция идёт через одну интеграцию: каналы (outbound и оба inbound) регистрируются
// в bank-a, поэтому её обслуживает flow-bank-a.
const string SimulationIntegration = "bank-a";

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

// pgweb — просмотр записей БД в браузере: http://localhost:8081.
// sslmode=disable обязателен: контейнер PostgreSQL в dev-контуре работает без TLS.
builder.AddContainer("pgweb", "sosedoff/pgweb")
    .WithHttpEndpoint(port: 8081, targetPort: 8081)
    .WithEnvironment("PGWEB_DATABASE_URL", ReferenceExpression.Create(
        $"postgres://postgres:{postgresPassword.Resource}@postgres:5432/gateway?sslmode=disable"))
    .WaitFor(postgres);

// Порядок запуска задан ожиданиями: management (healthy) -> все flow (healthy) -> product -> integration.
// Порт фиксирован: по нему продукт регистрирует конфигурацию без копирования ссылок.
var management = builder.AddProject<Projects.LtiIntegrationGateway_Management>("management")
    .WithHttpEndpoint(port: 5080, name: "http")
    .WithHttpHealthCheck("/health/ready")
    .WithReference(postgres)
    // Без ожидания БД сервис стартует раньше PostgreSQL и падает на миграциях.
    .WaitFor(postgres)
    .WithReplicas(2)
    .WithEnvironment("Observability__Otel__Enabled", "true")
    .WithEnvironment("Management__ApiKey", DevManagementApiKey);

// По экземпляру потоков на интеграцию; реплики одной интеграции делят её очередь.
var flowBankA = AddFlow(builder, management, postgres, SimulationIntegration);
//var flowBankB = AddFlow(builder, management, postgres, "bank-b");

WithSwaggerUrl(management);

// Симуляция живого контура: продукт стартует после шлюза и сам регистрирует каналы, контракты и маршрут.
var product = builder.AddProject<Projects.LtiIntegrationGateway_Simulation_Product>("product")
    .WithHttpEndpoint(port: 5081, name: "http")
    // Готовность продукта наступает после регистрации конфигурации в управляющем API.
    .WithHttpHealthCheck("/health/ready")
    .WithReference(postgres)
    .WaitFor(postgres)
    .WaitFor(management)
    .WaitFor(flowBankA)
    .WithEnvironment("Gateway__Integration", SimulationIntegration)
    .WithEnvironment("Gateway__ProductId", "product")
    .WithEnvironment("Management__BaseUrl", "http://localhost:5080")
    .WithEnvironment("Management__ApiKey", DevManagementApiKey)
    // Адрес внешней системы для outbound-канала: задан портом integration ниже.
    .WithEnvironment("Integration__BaseUrl", "https://localhost:5443")
    .WithEnvironment("Observability__Otel__Enabled", "true");

WithSwaggerUrl(product);

// Внешняя система стартует последней: шлюз и продукт к этому моменту уже готовы принимать вызовы.
// Оба входящих сценария (async и sync) идут через flow интеграции bank-a: она обслуживает
// inbound-каналы, зарегистрированные продуктом.
var integration = builder.AddProject<Projects.LtiIntegrationGateway_Simulation_Integration>("integration")
    .WithHttpEndpoint(port: 5082, name: "http")
    // Канал требует https: внешняя система слушает dev-сертификат на 5443.
    .WithHttpsEndpoint(port: 5443, name: "https")
    .WaitFor(product)
    .WithEnvironment("Gateway__BaseUrl", flowBankA.GetEndpoint("http"))
    .WithEnvironment("Observability__Otel__Enabled", "true");

WithSwaggerUrl(integration);

builder.Build().Run();

// Dashboard открывает Swagger вместо сырого адреса сервиса.
static IResourceBuilder<T> WithSwaggerUrl<T>(IResourceBuilder<T> resource, string endpointName = "http")
    where T : IResourceWithEndpoints =>
    resource.WithUrlForEndpoint(endpointName, url =>
    {
        url.DisplayText = "Swagger";
        url.Url = $"{url.Url!.TrimEnd('/')}/swagger";
    });

static IResourceBuilder<ProjectResource> AddFlow(
    IDistributedApplicationBuilder builder,
    IResourceBuilder<ProjectResource> management,
    IResourceBuilder<PostgresDatabaseResource> database,
    string integration)
{
    return WithSwaggerUrl(builder.AddProject<Projects.LtiIntegrationGateway_Flow>($"flow-{integration}")
        .WithHttpEndpoint()
        // Dashboard показывает состояние готовности: проба включает проверку хранилища.
        .WithHttpHealthCheck("/health/ready")
        .WithReference(database)
        .WaitFor(database)
        // Потоки стартуют только после готовности управляющего API.
        .WaitFor(management)
        .WithReplicas(2)
        .WithEnvironment("Gateway__Integration", integration)
        .WithEnvironment("Observability__Otel__Enabled", "true"));
}


