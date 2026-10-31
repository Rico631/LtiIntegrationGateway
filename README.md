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

## Входящий контур

Приём входящих вызовов реализован в `Infrastructure/Routing`, `Infrastructure/OpenApi` и
`Features/Inbound`:

- `InboundRouteCatalog` — активные наборы маршрутов по ключу ревизии; снапшот активных маршрутов
  неизменяем, при изменении отменяется токен изменения;
- `InboundRouteSetValidator` — проверка набора до активации: поддерживаемые методы
  (`GET`, `POST`, `PUT`, `PATCH`, `DELETE`), разбор шаблона, дубликаты «метод + шаблон» внутри набора
  и конфликты с активными наборами; при проблемах активация не меняет состояние;
- `InboundEndpointDataSource` — публикует маршруты как настоящие endpoint'ы ASP.NET Core
  с метаданными канала, ревизии, авторизации, квоты и политики ограничения частоты; источник
  добавляется в `IEndpointRouteBuilder.DataSources` (регистрации в DI недостаточно);
- `InboundRequestHandler` — принимает вызов, проверяет запрос, создаёт `InboundMessage` и передаёт
  его в очередь получателя через `IInboundMessagePublisher`;
- `InboundRequestAuthenticator` — `none`, `api_key`, `basic`, `client_certificate`;
- `InboundSchemaCache` — схемы тела, скомпилированные один раз на ревизию (`NJsonSchema` 11.6.1);
- `InboundMessageLogWriter` — журнал `InboundMessageLog`.

Порядок проверок и коды ответов (ADR §9.6):

| Проверка | Ответ |
| --- | --- |
| query-параметров больше 64 | `400` |
| тело непустое, `Content-Type` не указан или не объявлен схемой | `415` |
| тело непустое, схема тела не объявлена | `400` |
| секрет маршрута не настроен в конфигурации | `503` |
| аутентификация не пройдена (тело ответа пустое) | `401` |
| тело не разбирается как JSON или глубина JSON больше 64 | `400` |
| тело не соответствует схеме операции | `422` |
| сообщение передано в очередь получателя | `202` |
| передача не удалась | `503` |

Настройки приёма задаются на версии контракта
(`PATCH /v1/channels/{name}/contracts/{v}/settings`): `recipient_queue`, `auth_type`
(`none`, `api_key`, `basic`, `client_certificate`), `config_key`, `header_name`, `user_name`,
`rate_limit_permit`, `rate_limit_window_seconds`. Значения секретов хранятся только в конфигурации
(`Channels__...` или переменные окружения) и не попадают ни в базу, ни в журнал, ни в ответы.

Схемы тела извлекаются при публикации: локальные `$ref` разворачиваются (глубина до 32, цикл —
отказ публикации), поэтому сохранённая схема самодостаточна. Квота частоты берётся из маршрута,
при её отсутствии — из `Inbound:RateLimit:PermitLimit` и `Inbound:RateLimit:WindowSeconds`;
превышение — `429`.

Журнал: каждый принятый и каждый отклонённый вызов пишется в `InboundMessageLogs` (тело, метод,
фактический путь, шаблон, `operationId`, query-параметры, статус `pending → sent | failed`, код
ошибки, ревизия конфигурации, время получения). Запись `pending` создаётся до `Send`, поэтому сбой
передачи виден в журнале.

Не реализовано: настройки отдельной операции, флаг `enabled`, HMAC-подпись, дедупликация входящих
вызовов, пул HTTP-обработчиков для `client_certificate`, фоновый диспетчер результатов.


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

## Управляющий API

Управляющие операции публикуются из Protobuf-контракта `lti.gateway.management.v1`
(`Protos/v1/management_api.proto`): один сервис `ManagementService`, доступный как gRPC и как REST
через JSON transcoding. Реализация — `Features/Management`, доменные правила остаются в срезах
`Features/Channels`, `Features/ProductRoutes`, `Features/Contracts`.

Аутентификация: заголовок `X-Management-Api-Key` (имя настраивается), значение — переменная
окружения `Management__ApiKey`. Пустой ключ в конфигурации означает `503` для всего управляющего API;
отсутствие или несовпадение ключа — `401` без деталей (для gRPC — `Unauthenticated`). Сравнение
выполняется за постоянное время.

Основные операции (полный перечень — в ADR §15.2):

| REST | Назначение |
| --- | --- |
| `GET /v1/channels` | список каналов |
| `POST /v1/channels` | создание канала с черновиком |
| `PATCH /v1/channels/{name}/draft` | изменение черновика |
| `POST /v1/channels/{name}/revisions/{n}/publish` | публикация ревизии |
| `POST /v1/channels/{name}/revisions/{n}/activate` | активация ревизии |
| `POST /v1/channels/{name}/rollback` | откат |
| `PUT /v1/products/{id}/route` | маршрут продукта |
| `POST /v1/channels/{name}/contracts` | загрузка OpenAPI-документа |
| `POST /v1/channels/{name}/contracts/{v}/publish` | публикация версии контракта |
| `POST /v1/channels/{name}/contracts/{v}/activate` | активация версии и регистрация маршрутов |
| `GET /v1/channels/{name}/contracts/{v}/operations` | операции версии контракта |
| `GET /v1/channels/{name}/contracts/{v}` | версия контракта с настройками приёма |
| `PATCH /v1/channels/{name}/contracts/{v}/settings` | настройки приёма черновика |
| `GET /v1/operations/{messageId}` | статус исходящей операции |

Контракты: `Infrastructure/OpenApi` читает документ (`Microsoft.OpenApi` 3.11.0) и проверяет его
fail-closed — принимается OpenAPI 3.0, Swagger 2.0 конвертируется (факт фиксируется в аудите),
OpenAPI 3.1 отклоняется; запрещённые конструкции, внешние `$ref` и поля рядом с `$ref` дают отказ
со списком проблем, неизвестный `format` — предупреждение. При публикации извлекаются схемы тела
операций, а версия без очереди получателя отклоняется с кодом `queue`. Активация версии регистрирует
её маршруты в каталоге входящих маршрутов, собирает схемы в кэш и снимает маршруты предыдущей версии.

Аудит: каждая управляющая операция (включая отклонённые и чтение) пишется в
`ManagementAuditEntries` с операцией, объектом и результатом; значения секретов не сохраняются.

Не реализовано: настройки отдельной операции (`enabled`, очередь и лимит частоты на операцию),
состояние канала (`/v1/channels/{name}/health`), персональная идентичность администратора (JWT/IdP).

## Структура

```text
src/LtiIntegrationGateway           производственный код вместе с Protos/v1/*.proto
src/LtiIntegrationGateway.Tests     тесты на TUnit
docs/adr                            архитектурные решения
openspec                            спецификации и изменения
```
