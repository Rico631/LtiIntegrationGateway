# Архитектура решения

Документ описывает **фактическую организацию кода** `LtiIntegrationGateway`: состав решения, карту
каталогов, вертикальные срезы, инфраструктурные компоненты, потоки данных и принятые соглашения.

Источники:

- требования и архитектурные решения — `docs/adr/0001-integration-gateway.md` (ссылки вида «ADR §8.7»);
- правила ведения артефактов OpenSpec — `openspec/config.yaml`;
- сборка, запуск и эксплуатация — `README.md`;
- обязательная документация контрактов — `AGENTS.md`.

**Актуальность.** Файл отражает текущее состояние кода и обновляется после архивации каждого
изменения (правило `operations.archive.guidance` в `openspec/config.yaml`). Разделы 1–9 описывают
то, что уже есть в репозитории; плановые элементы ADR сюда не переносятся.

## 1. Состав решения

| Элемент | Назначение |
|---|---|
| `LtiIntegrationGateway.slnx` | решение; оба проекта подключены |
| `src/LtiIntegrationGateway` | весь производственный код (`Microsoft.NET.Sdk.Web`, `net10.0`) |
| `src/LtiIntegrationGateway.Tests` | тесты на TUnit (`Microsoft.Testing.Platform`) |
| `global.json` | включает runner MTP для `dotnet test` на .NET 10 |

Ключевые пакеты производственного проекта: `Google.Protobuf`, `Grpc.Tools` (генерация из `.proto`),
`Grpc.AspNetCore` + `Microsoft.AspNetCore.Grpc.JsonTranscoding` (gRPC и REST из одного контракта),
`Microsoft.EntityFrameworkCore.Sqlite` + `Design`, `Microsoft.OpenApi` (чтение контрактов),
`Rebus` + `Rebus.ServiceProvider`.

Тестовый проект дополнительно использует `Microsoft.AspNetCore.Mvc.Testing`, `Grpc.Net.Client`.

## 2. Точка сборки приложения

`Program.cs` — единственная композиционная точка; порядок регистрации значим.

| Шаг | Что делает |
|---|---|
| `AddDbContext<GatewayDbContext>` | SQLite по `ConnectionStrings:Gateway`, без строки — исключение |
| `AddHealthChecks`, `AddGatewayRebus` | liveness и шина с ProtoJSON-сериализатором |
| Регистрация срезов `Features/Channels` | валидатор и шесть сценариев жизненного цикла |
| Регистрация outbound-шагов | `ISecretStore`, маршруты продуктов, шаги проверки и вызова |
| `AddHttpClient(ExecuteHttpCallHandler.ClientName)` | `AllowAutoRedirect = false` (ADR §8.7) |
| Каталог входящих маршрутов и источник endpoint'ов | синглтоны `InboundRouteCatalog`, `InboundEndpointDataSource` |
| `AddRateLimiter` | политика `inbound-per-channel`, `429` при превышении |
| Контракты и управляющий API | reader OpenAPI, четыре среза контрактов, `ManagementApiService` |
| `AddGrpc().AddJsonTranscoding()` | публикация управляющего сервиса как gRPC и REST |
| Fail-fast проверка строки подключения | выполняется **после** `builder.Build()` |
| `Database.Migrate()` | миграции применяются при старте |
| `UseRateLimiter`, `UseMiddleware<ManagementApiKeyMiddleware>` | ограничение частоты и ключ API |
| `((IEndpointRouteBuilder)app).DataSources.Add(...)` | динамические маршруты: регистрации в DI недостаточно |
| `MapGrpcService<ManagementApiService>`, `MapHealthChecks` | управляющий сервис и `/health/live` |

Инварианты: fail-fast проверка конфигурации — после `Build()` (иначе тестовые переопределения не
видны); миграции применяются автоматически; `/health/live` не требует ключа управляющего API.

## 3. Карта каталогов

| Каталог | Содержимое | Где описано |
|---|---|---|
| `Protos/v1` | интеграционные сообщения `lti.gateway.messages.v1` и управляющий API `lti.gateway.management.v1` | ADR §5, §8.3–8.5, §9.3, §15.2 |
| `Protos/google/api` | вендорные `annotations.proto` и `http.proto` для HTTP-аннотаций; не редактируются, C#-типы берутся из `Google.Api.CommonProtos` | ADR §5.1 |
| `Contracts` | `MessageContractValidator` — проверка обязательных полей `OutboundRequest` | spec `message-contracts` |
| `Features/Channels` | канал и его ревизии: создание, черновик, публикация, активация, откат, чтение | spec `channel-registry` |
| `Features/ProductRoutes` | маршруты продуктов: разрешённые каналы и адреса очередей | spec `outbound-dispatch` |
| `Features/Outbound` | обработка `OutboundRequest`: проверки, авторизация, HTTP с повторами, операция и журнал, публикация результата | spec `outbound-dispatch` |
| `Features/Contracts` | версии OpenAPI-контрактов: загрузка, публикация, активация, чтение операций | spec `contract-registry` |
| `Features/Inbound` | обработчик динамических входящих маршрутов (диагностический ответ спайка) | spec `inbound-routing` |
| `Features/Management` | gRPC-фасад управляющего API и маппинг proto ↔ домен | spec `management-api` |
| `Infrastructure/Persistence` | `GatewayDbContext`, сущности, миграции | ADR §11 |
| `Infrastructure/Rebus` | `ProtobufSerializer`, `ProtobufMessageRegistry`, конфигурация шины | ADR §5.3, §10 |
| `Infrastructure/OpenApi` | чтение документа и fail-closed проверки | ADR §14 |
| `Infrastructure/Routing` | каталог маршрутов, источник endpoint'ов, валидатор набора, лимит частоты | ADR §5.2 |
| `Infrastructure/Security` | `ISecretStore` и реализация поверх `IConfiguration` | ADR §7.5, §13 |
| `Infrastructure/Management` | ключ управляющего API, аудит управляющих операций | ADR §13, §15.1 |

`bin`, `obj`, `Properties` — служебные, в архитектуре не участвуют.

## 4. Вертикальные срезы

Каждый сценарий — отдельный каталог с командой/запросом, результатом и обработчиком; общих
репозиториев и медиатора нет (ADR §4.2).

### Каналы (spec `channel-registry`)

```text
Features/Channels
├── CreateChannel            CreateChannelHandler
├── UpdateChannelDraft       UpdateChannelDraftHandler
├── PublishChannelRevision   PublishChannelRevisionHandler
├── ActivateChannelRevision  ActivateChannelRevisionHandler
├── RollbackChannelRevision  RollbackChannelRevisionHandler
├── GetChannel               GetChannelHandler
├── ChannelConfiguration      настройки ревизии (record)
├── ChannelConfigurationValidator  проверки публикации
├── ChannelRevisionWriter     перенос настроек в сущность и обратно
└── ChannelAuditWriter        аудит изменения в транзакции среза
```

### Маршруты продуктов и исходящий контур (spec `outbound-dispatch`)

```text
Features/ProductRoutes
├── RegisterProductRoute     upsert каналов и адресов продукта
└── GetProductRoute          чтение маршрута

Features/Outbound
├── OutboundRequestHandler        адаптер Rebus: заголовки → команда
├── ProcessOutboundRequestHandler сценарий: порядок проверок и вызов
├── ResolveSenderIdentity         product_id + rbs2-sender-address по ProductRoutes
├── LoadActiveChannel             активная ревизия канала
├── RelativePathMatcher           структурные запреты и шаблоны пути
├── ResolveAuthorization          none / bearer / api_key / basic
├── ExecuteHttpCall               HTTP с повторами по политике канала
├── OutboundOperationRecorder     операция и журнал одной транзакцией
├── PublishOutboundResult         Send результата и признак доставки
└── OutboundErrorCodes            коды отказов и транспортных ошибок
```

### Контракты (spec `contract-registry`)

```text
Features/Contracts
├── UploadContractVersion      документ → черновик версии
├── PublishContractVersion     операции, дубли operationId, конфликты шаблонов
├── ActivateContractVersion    каталог маршрутов → транзакция БД
├── ListContractOperations     операции версии
└── ContractRouteSets          InboundRouteSet из операций версии
```

### Входящая маршрутизация (spec `inbound-routing`)

```text
Features/Inbound
└── InboundProbeHandler        диагностический ответ динамического маршрута

Infrastructure/Routing
├── InboundRouteDefinition     маршрут, набор ревизии, результат активации
├── InboundRouteCatalog        активные наборы, снапшот, токен изменения
├── InboundRouteSetValidator   методы, шаблоны, конфликты
├── InboundEndpointDataSource  RouteEndpoint'ы и перестройка matcher
├── InboundChannelMetadata     канал как ключ лимита частоты
└── InboundRateLimitOptions    квота и окно
```

### Управляющий API (spec `management-api`)

```text
Features/Management
├── ManagementApiService              gRPC-фасад: зависимости и общие помощники
├── ManagementApiService.Channels     каналы и ревизии
├── ManagementApiService.Catalog      маршруты продуктов, статус операции
├── ManagementApiService.Contracts    версии контрактов
└── ChannelMapping                    proto ↔ ChannelConfiguration
```

Фасад не содержит доменных правил: он вызывает срезы, отображает результаты в proto-сообщения,
возвращает `NotFound` для отсутствующих объектов и пишет аудит управляющих операций.

## 5. Инфраструктурные компоненты

| Компонент | Ответственность | Особенности |
|---|---|---|
| `Persistence/GatewayDbContext` | модель данных и конфигурация EF Core | уникальные индексы, каскады, конвертация enum и политики повторов в строку/JSON |
| `Persistence/Entities` | каналы и ревизии, маршруты продуктов, контракты, операции и журналы | опубликованные ревизии неизменяемы |
| `Persistence/Migrations` | `InitialChannelRegistry`, `AddOutboundDispatch`, `AddContractRegistry` | применяются при старте |
| `Rebus/ProtobufSerializer` | `ISerializer` на ProtoJSON | заголовки `rbs2-msg-type` и `rbs2-content-type` |
| `Rebus/ProtobufMessageRegistry` | реестр типов сообщений | неизвестный тип — диагностируемая ошибка |
| `Rebus/RebusConfigurationExtensions` | регистрация шины и обработчика | очередь `lti.gateway.outbound` |
| `OpenApi/MicrosoftOpenApiDocumentReader` | чтение документа, версия, синтаксис | `Microsoft.OpenApi` 3.11.0 (ADR §14) |
| `OpenApi/OpenApiDocumentValidator` | fail-closed проверки и извлечение операций | запрещённые ключевые слова, `$ref`, `format` |
| `Management/ManagementApiKeyMiddleware` | ключ управляющего API | `401`/`503`, gRPC `Unauthenticated` |
| `Management/ManagementAuditWriter` | аудит управляющих операций | без значений секретов |
| `Security/ConfigurationSecretStore` | значение секрета по ключу | только `IConfiguration` |

## 6. Контракты и генерация кода

- `Protos/v1/integration_messages.proto` — сообщения Rebus (`lti.gateway.messages.v1`):
  `OutboundRequest`, `OutboundResult`, `InboundMessage`, `QueryParameter`, `Outcome`.
- `Protos/v1/management_api.proto` — управляющий API (`lti.gateway.management.v1`): один сервис
  `ManagementService`, HTTP-аннотации для REST через transcoding.
- `Protos/google/api/*.proto` — вендорные дескрипторы; подключаются только как путь импорта,
  C#-типы берутся из `Google.Api.CommonProtos` (иначе конфликт типов CS0436).
- Генерация выполняется при сборке (`Grpc.Tools`); сгенерированные файлы не редактируются и в
  репозитории не хранятся.
- Обязательное требование к документации контрактов — `AGENTS.md`.

## 7. Потоки и транзакционные границы

**Outbound.** `OutboundRequest` → проверки (поля, продукт и адрес отправителя, адрес доставки,
идемпотентность, активная ревизия канала, метод и путь, авторизация) → операция `Accepted` →
`InProgress` → HTTP-вызов с повторами → **одна транзакция**: операция `Completed` + запись журнала
`pending` → `Send` результата → журнал `sent`. Отказы фиксируются в журнале и возвращаются продукту
(ADR §8, spec `outbound-dispatch`).

**Контракты.** Загрузка создаёт черновик версии; публикация извлекает операции и проверяет
конфликты; активация сначала регистрирует набор в каталоге маршрутов, затем одной транзакцией меняет
статусы версий и снимает набор предыдущей версии; при проблемах каталога база не меняется
(ADR §5.2, spec `contract-registry`).

**Каналы.** Ревизия — агрегат настроек: правки идут в черновик, публикация фиксирует неизменяемую
ревизию, активация делает её действующей, откат возвращает предыдущую (spec `channel-registry`).

**Аудит.** `ChannelAuditEntry` пишется в транзакции доменного среза; `ManagementAuditEntry`
фиксирует факт управляющего вызова, включая отклонённые и читающие операции.

## 8. Соглашения и повторяемые решения

| Решение | Причина |
|---|---|
| Ревизия — агрегат настроек, в `Channel` настроек нет | неизменяемость опубликованного и безопасный откат |
| Результат операции — запись с `Problems`, а не исключение | отказ домена не является сбоем, список проблем возвращается целиком |
| Новые дочерние сущности добавляются через `DbSet`, а не только в навигацию | клиентский Guid-ключ: EF считает их `Modified`, `UPDATE` не находит строку |
| Сортировка по `DateTimeOffset` не используется в SQLite-запросах | SQLite не поддерживает такие `ORDER BY` |
| Строковые поля proto заполняются только непустыми значениями | присваивание `null` бросает `ArgumentNullException` |
| Динамические endpoint'ы добавляются в `IEndpointRouteBuilder.DataSources` | регистрации `EndpointDataSource` в DI недостаточно (ADR §5.2) |
| gRPC-сервисы публикуются в корне | transcoding сохраняет пути `/v1/...`; следствие — двухсегментные inbound-маршруты отвечают `404` вместо `405` (ADR §15.2) |
| Секреты читаются только через `ISecretStore` | в базе и API хранятся лишь ключи конфигурации |

## 9. Тесты

Структура повторяет производственные каталоги; общая обвязка — `TestSupport`.

| Каталог | Что проверяется |
|---|---|
| `Contracts` | round-trip ProtoJSON, профиль кодирования, дескрипторы, `MessageContractValidator` |
| `Features/Host` | liveness, конфигурация хранилища, отсутствие демо-API, регистрация обработчиков |
| `Features/Channels` | валидатор настроек, жизненный цикл ревизий, сохранение до `DbContext` |
| `Features/Outbound` | успешный вызов, отказы, авторизация, повторы, идемпотентность |
| `Features/Inbound` | активация и деактивация маршрутов, конфликты, приоритеты, лимит частоты |
| `Features/Contracts` | загрузка и проверка документов, публикация, активация маршрутов |
| `Features/Management` | ключ API, паритет gRPC/REST, каналы, маршруты, статус операции |
| `Infrastructure/*` | сериализация и реестр Rebus, `DbContext`, каталог маршрутов, чтение OpenAPI |

`TestSupport`:

- `SqliteInMemoryDatabase` — SQLite `:memory:` с открытым соединением на время теста;
- `GatewayWebApplicationFactory` — хост с подменой строки подключения и дополнительными ключами
  конфигурации;
- `OutboundFixture` — SQLite, тестовый HTTP-обработчик и два in-memory endpoint'а Rebus;
- `ManagementApi` — REST-клиент с ключом и in-process gRPC-канал;
- `OpenApiSamples`, `RecordingHttpMessageHandler`, `ContractJson`, `InMemoryLogger*`.

Запуск: `dotnet test LtiIntegrationGateway.slnx` (через `Microsoft.Testing.Platform`, см. `global.json`).

## 10. Соответствие архивным изменениям

Каждый блок кода появился из архивированного изменения; полный контекст — в
`openspec/changes/archive/<change>/` и в постоянных спеках `openspec/specs/`.

| Изменение | Capability | Что появилось в коде |
|---|---|---|
| `2026-10-29-bootstrap-solution-skeleton` | `gateway-host` | хост, `/health/live`, EF Core + миграции, fail-fast конфигурации |
| `2026-10-29-define-integration-contracts` | `message-contracts` | `integration_messages.proto`, `MessageContractValidator` |
| `2026-10-29-rebus-protobuf-serialization` | `message-serialization` | `Infrastructure/Rebus` (сериализатор, реестр, конфигурация) |
| `2026-10-29-channel-registry` | `channel-registry` | `Features/Channels`, сущности каналов и ревизий, первая миграция |
| `2026-10-29-outbound-dispatch` | `outbound-dispatch` | `Features/Outbound`, `Features/ProductRoutes`, `Infrastructure/Security`, операции и журнал |
| `2026-10-29-inbound-routing-spike` | `inbound-routing` | `Infrastructure/Routing`, `Features/Inbound`, лимит частоты |
| `2026-10-29-management-grpc-api` | `management-api`, `contract-registry` | `Protos/v1/management_api.proto`, `Features/Management`, `Features/Contracts`, `Infrastructure/OpenApi`, `Infrastructure/Management`, миграция контрактов |

## 11. Поддержание актуальности

После архивации изменения:

1. обновить разделы 1–9 под фактическое состояние кода (новые каталоги, срезы, сущности, миграции,
   соглашения);
2. добавить строку в таблицу раздела 10;
3. удалить описания того, что было удалено или переименовано;
4. проверить, что упомянутые пути существуют и ссылки на ADR/спеки указывают на актуальные разделы.

Требования и правила в этот файл не переносятся — здесь фиксируется только фактическая организация
кода.
