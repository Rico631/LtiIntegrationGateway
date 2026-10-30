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

## Контракты

Контракт обмена сообщениями между продуктами и шлюзом — один файл:
`src/LtiIntegrationGateway/Protos/v1/integration_messages.proto`
(`package lti.gateway.messages.v1`). Потребитель забирает `.proto` и генерирует типы
самостоятельно; пакет контрактов не публикуется.

- C#-типы генерируются при сборке (`dotnet build`), вручную не редактируются;
- профиль кодирования на проводе (ProtoJSON, camelCase, `bytes` → base64, неизвестные поля
  игнорируются, `rbs2-msg-type` = полное имя сообщения) описан в шапке `.proto` и является
  обязательным для всех потребителей;
- обязательные поля `OutboundRequest` проверяются `Contracts/MessageContractValidator`.

## Шина

Rebus подключён через `Infrastructure/Rebus/RebusConfigurationExtensions.AddGatewayRebus()`
(вызов из `Program.cs`):

- входная очередь шлюза — `lti.gateway.outbound`;
- транспорт — in-memory (стартовый вариант, ADR §10.1);
- сериализатор — собственный `Infrastructure/Rebus/ProtobufSerializer`, а не стандартный
  JSON-сериализатор Rebus: тело — ProtoJSON по профилю из `.proto`, тип определяется заголовком
  `rbs2-msg-type`;
- реестр типов — `Infrastructure/Rebus/ProtobufMessageRegistry`, покрывает все сообщения
  контракта `lti.gateway.messages.v1`.

Ограничения текущего этапа: маршрутизация между продуктами и политика повторов на уровне шины не
настроены; error-queue остаётся значением по умолчанию Rebus — `lti.gateway.outbound.error`
(расхождение с ADR §10.2 зафиксировано как открытый вопрос).

## Исходящий контур

Обработчик `Features/Outbound/OutboundRequestHandler` подписан на `OutboundRequest` в очереди
`lti.gateway.outbound` и выполняет шаги в фиксированном порядке:

1. обязательные поля запроса (`Contracts/MessageContractValidator`);
2. `product_id` и заголовок `rbs2-sender-address` по конфигурации `ProductRoutes`
   (`Features/ProductRoutes`), включая разрешённость канала продукту;
3. адрес доставки результата (`rbs2-return-address`) — только из разрешённых продукту;
4. идемпотентность по `message_id` (уникальный индекс `OutboundOperations.MessageId`);
5. активная ревизия канала, разрешённый метод и путь (структурные запреты + шаблоны);
6. авторизация из конфигурации (`none`, `bearer`, `api_key` — заголовок или query-параметр,
   `basic`);
7. HTTP-вызов с повторами по политике ревизии (`AllowAutoRedirect = false`).

Исходы и коды в `OutboundResult`:

| `outcome`         | коды                                                                                                     |
| ----------------- | -------------------------------------------------------------------------------------------------------- |
| `GATEWAY_REFUSED` | `PRODUCT_NOT_ALLOWED`, `CHANNEL_NOT_ALLOWED`, `CHANNEL_NOT_ACTIVE`, `METHOD_NOT_ALLOWED`, `PATH_NOT_ALLOWED`, `SECRET_NOT_CONFIGURED`, `AUTH_TYPE_UNSUPPORTED` |
| `TRANSPORT_ERROR` | `TIMEOUT`, `CONNECTION_FAILED`, `TLS_FAILED`, `RETRIES_EXHAUSTED`                                        |
| `HTTP_RESPONSE`   | статус и тело ответа внешней системы; ошибки `4xx`/`5xx` не приводят к повтору на уровне шины            |

Повторы задаются политикой канала (`ChannelRevision.RetryPolicy`): правила `http_status_in` и
`transport_error`, массив задержек в миллисекундах, учёт `Retry-After` и общего предела
`OverallCapMs`; таймаут — на попытку. `POST` и `PATCH` повторяются только при непустом
`idempotency_key`. Отсутствующее значение секрета — отказ `SECRET_NOT_CONFIGURED`, тип
`client_certificate` — `AUTH_TYPE_UNSUPPORTED`.

Состояние операции и журнал:

- `OutboundOperations` — состояние (`Accepted`/`InProgress`/`Completed`), исход, число попыток,
  адрес доставки; `MessageId` уникален, повторный запрос не вызывает внешнюю систему;
- `OutboundMessageLogs` — тела запроса и ответа, статус, исход, `DeliveryStatus`
  (`Pending` → `Sent` после отправки результата в очередь продукта).

Запись операции и журнала фиксируется до отправки результата; значения секретов не попадают ни в
журнал, ни в результат.

Не реализовано на этом этапе: `client_certificate`, пул HTTP-обработчиков по отпечатку
сертификата, фоновый диспетчер доставки результатов, OAuth2 client credentials, управляющий API
маршрутов продуктов (срезы вызываются из кода и тестов).

## Динамические входящие маршруты

Спайк динамической маршрутизации (ADR §5.2) реализован в `Infrastructure/Routing` и
`Features/Inbound`:

- `InboundRouteCatalog` — активные наборы маршрутов по ключу ревизии; снапшот активных маршрутов
  неизменяем, при изменении отменяется токен изменения;
- `InboundRouteSetValidator` — проверка набора до активации: поддерживаемые методы
  (`GET`, `POST`, `PUT`, `PATCH`, `DELETE`), разбор шаблона, дубликаты «метод + шаблон» внутри набора
  и конфликты с активными наборами; при проблемах активация не меняет состояние;
- `InboundEndpointDataSource` — публикует маршруты как настоящие endpoint'ы ASP.NET Core
  с метаданными метода, канала и политики ограничения частоты; источник добавляется в
  `IEndpointRouteBuilder.DataSources` (регистрации в DI недостаточно);
- `InboundProbeHandler` — **диагностический** ответ `200` с методом, шаблоном, путём и каналом;
  сообщения в шину не публикуются. Это временный контракт спайка: в inbound-контуре он заменяется
  на `202`/`503` вместе с публикацией `InboundMessage`.

Ограничение частоты: политика `inbound-per-channel` (`AddRateLimiter` + `UseRateLimiter`),
fixed window с партицией по каналу, лимиты — `Inbound:RateLimit:PermitLimit` и
`Inbound:RateLimit:WindowSeconds`, превышение — `429`.

Результат спайка и найденные особенности (DI-регистрация источника, `IChangeToken`, `405` при
несовпадении метода) зафиксированы в ADR §5.2. Не реализовано: загрузка OpenAPI, валидация тела,
публикация `InboundMessage`, ответы `202`/`503`, авторизация входящих вызовов.


## Конфигурация каналов

Модель канала хранится в `Infrastructure/Persistence/Entities`:

- `Channel` — имя канала (уникальное);
- `ChannelRevision` — набор настроек: базовый URL, тип авторизации и ключ конфигурации,
  допустимые методы и шаблоны путей, политика повторов, таймауты;
- `ChannelAllowedMethod`, `ChannelAllowedPath` — разрешённые методы и шаблоны путей ревизии;
- `ChannelAuditEntry` — аудит изменений конфигурации.

Жизненный цикл ревизии: `Draft` → `Published` → `Active` → `Superseded`. Опубликованная
ревизия неизменяема, изменение настроек — только через новую ревизию; откат активирует
предыдущую. Срезы жизненного цикла находятся в `Features/Channels`.

Проверки перед публикацией: базовый URL — абсолютный `https` без учётных данных, методы — из
фиксированного набора, шаблоны путей — по правилам ADR §8.7, политика повторов — 1..5 задержек
на правило. Отклонённая публикация возвращает список причин и не меняет активную ревизию.

Значения секретов в базе отсутствуют: хранятся только тип авторизации, ключ конфигурации и
несекретные параметры; сами значения берутся из переменных окружения.

Миграции EF Core применяются при старте приложения (`Database.Migrate()`). Новая миграция:

```bash
dotnet ef migrations add <Name> --project src/LtiIntegrationGateway --output-dir Infrastructure/Persistence/Migrations
```

Управляющий API для каналов пока не реализован — срезы вызываются из кода и тестов.

## Структура

```text
src/LtiIntegrationGateway           производственный код вместе с Protos/v1/*.proto
src/LtiIntegrationGateway.Tests     тесты на TUnit
docs/adr                            архитектурные решения
openspec                            спецификации и изменения
```
