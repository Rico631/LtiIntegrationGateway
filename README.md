# LtiIntegrationGateway

Шлюз интеграции продуктов LTI с внешними системами: принимает и передаёт данные через
Rebus, выполняет исходящие HTTP-вызовы по конфигурации каналов, валидирует входящие запросы
по OpenAPI.

Архитектура и требования: [`docs/adr/0001-integration-gateway.md`](docs/adr/0001-integration-gateway.md).
Планирование изменений ведётся в OpenSpec (`openspec/`).

## Требования

- .NET SDK 10.0
- `global.json` включает новый режим `dotnet test` для Microsoft.Testing.Platform (TUnit),
  иначе `dotnet test` на .NET 10 SDK не сможет запустить тесты MTP

## Обязательная настройка

Приложение не стартует без строки подключения к хранилищу:

```json
{
  "ConnectionStrings": {
    "Gateway": "Data Source=gateway.db"
  }
}
```

Ключ `ConnectionStrings:Gateway` задаётся в `appsettings.json`, в
`appsettings.Development.json` или через переменную окружения
`ConnectionStrings__Gateway`. Если ключ отсутствует или пуст, запуск завершается ошибкой с
именем ключа — это ожидаемое поведение, а не дефект.

## Команды

```bash
# восстановление и сборка
dotnet build LtiIntegrationGateway.slnx

# тесты (TUnit, Microsoft.Testing.Platform)
dotnet test LtiIntegrationGateway.slnx

# запуск приложения
dotnet run --project src/LtiIntegrationGateway
```

## Эндпоинты

| Метод | Путь | Назначение |
|---|---|---|
| `GET` | `/health/live` | liveness-проба: `200`, не обращается к БД, Rebus и внешним системам |

Readiness-проба с проверками зависимостей, метрики и OTel пока не реализованы (см. раздел 20
ADR).

## Структура

```text
src/LtiIntegrationGateway           производственный код
src/LtiIntegrationGateway.Tests     тесты на TUnit
docs/adr                            архитектурные решения
openspec                            спецификации и изменения
```
