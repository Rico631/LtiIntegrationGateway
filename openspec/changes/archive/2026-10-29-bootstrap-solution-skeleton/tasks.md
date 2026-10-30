# Tasks

## 1. Очистка шаблонного кода

- [x] 1.1 Удалить из `src/LtiIntegrationGateway/Program.cs` регистрацию OpenAPI
      (`AddOpenApi`, `MapOpenApi`), `UseHttpsRedirection`, массив `summaries`,
      `MapGet("/weatherforecast")` и запись `WeatherForecast`
- [x] 1.2 Удалить из `LtiIntegrationGateway.csproj` ссылку на пакет
      `Microsoft.AspNetCore.OpenApi`
- [x] 1.3 Заменить содержимое `src/LtiIntegrationGateway/LtiIntegrationGateway.http` на
      единственный запрос `GET {{HostAddress}}/health/live`

## 2. Конфигурация хранилища

- [x] 2.1 Добавить пакет `Microsoft.EntityFrameworkCore.Sqlite` версии 10.0.14 в
      `LtiIntegrationGateway.csproj`
- [x] 2.2 Добавить `Infrastructure/Persistence/GatewayDbContext.cs`: пустой контекст без
      сущностей, `OnModelCreating` не переопределяется; в комментарии зафиксировать, что
      сущности добавляются только срезом, который их использует
- [x] 2.3 Добавить в `appsettings.json` ключ `ConnectionStrings:Gateway` со значением
      `Data Source=gateway.db`, в `appsettings.Development.json` — путь для локального запуска
- [x] 2.4 Добавить в `Program.cs` проверку строки подключения: отсутствие или пустое значение
      выбрасывает `InvalidOperationException` с текстом, содержащим `ConnectionStrings:Gateway`,
      до `builder.Build()`
- [x] 2.5 Зарегистрировать `AddDbContext<GatewayDbContext>` с `UseSqlite(connectionString)`

## 3. Liveness-проба

- [x] 3.1 Зарегистрировать `AddHealthChecks()` в `Program.cs`
- [x] 3.2 Замапить `GET /health/live` через `MapHealthChecks` с
      `new HealthCheckOptions { Predicate = _ => false }`, чтобы проба не зависела от
      зарегистрированных проверок
- [x] 3.3 Добавить `public partial class Program;` в конец `Program.cs` для
      `WebApplicationFactory<Program>`

## 4. Тестовый проект и TestSupport

- [x] 4.1 Создать проект `src/LtiIntegrationGateway.Tests` на основе шаблона TUnit
      (`TUnit.Templates` версии 1.76.45), зафиксировать версию `TUnit` в файле проекта
- [x] 4.2 Добавить `ProjectReference` на `src/LtiIntegrationGateway/LtiIntegrationGateway.csproj`
- [x] 4.3 Добавить пакет `Microsoft.AspNetCore.Mvc.Testing` версии 10.0.14
- [x] 4.4 Подключить проект к решению:
      `dotnet sln LtiIntegrationGateway.slnx add src\LtiIntegrationGateway.Tests\LtiIntegrationGateway.Tests.csproj`
- [x] 4.5 Создать каталоги `Features/`, `Infrastructure/`, `TestSupport/` в тестовом проекте
- [x] 4.6 Реализовать `TestSupport/SqliteInMemoryDatabase.cs`: соединение
      `Data Source=:memory:` остаётся открытым на всё время жизни объекта, `Dispose` закрывает
      его; метод возвращает `DbContextOptions<GatewayDbContext>`
- [x] 4.7 Реализовать `TestSupport/GatewayWebApplicationFactory.cs` на базе
      `WebApplicationFactory<Program>`: переопределяет `ConnectionStrings:Gateway` на
      in-memory SQLite и умеет собираться без этой настройки
- [x] 4.8 Реализовать `TestSupport/InMemoryLoggerProvider.cs` и `TestSupport/InMemoryLogger.cs`
      для перехвата записей лога в тестах

## 5. Тесты поведения хоста

- [x] 5.1 `Features/Host/LivenessTests.cs`: `GET /health/live` возвращает `200` при запущенном
      хосте без настроек Rebus, каналов и внешних систем
- [x] 5.2 `Features/Host/DemoApiRemovalTests.cs`: `GET /weatherforecast` возвращает `404`
- [x] 5.3 `Features/Host/StorageConfigurationTests.cs`: старт без `ConnectionStrings:Gateway`
      завершается исключением, текст содержит имя ключа; старт с заданной строкой успешен
- [x] 5.4 `Infrastructure/Persistence/GatewayDbContextTests.cs`: контекст создаётся на
      SQLite `:memory:` и остаётся работоспособным между двумя последовательными операциями в
      рамках одного теста (соединение не закрывается)
- [x] 5.5 `Features/Host/StartupLoggingTests.cs`: записи лога запуска не содержат значения
      строки подключения

## 6. Проверка и документация

- [x] 6.1 Выполнить `dotnet build LtiIntegrationGateway.slnx` и убедиться в отсутствии ошибок
- [x] 6.2 Выполнить `dotnet test` и убедиться, что все тесты проходят
- [x] 6.3 Ручная проверка: запустить приложение, `GET /health/live` → `200`,
      `GET /weatherforecast` → `404`
- [x] 6.4 Обновить `README.md`: обязательная настройка `ConnectionStrings:Gateway`, команды
      сборки, запуска и прогона тестов
- [x] 6.5 В итоговом отчёте указать фактически выполненные команды и их результаты; отдельно
      отметить, что readiness-проба, метрики и OTel не реализованы (ADR §20)
