# Архитектура LtiIntegrationGateway

Документ фиксирует **архитектурный контекст, границы ответственности, принципы и ограничения**
`LtiIntegrationGateway`, а также фактическую организацию кода, инфраструктурные компоненты и
потоки выполнения. Это актуальное описание системы, а не журнал истории решений.

## Источники истины и границы документации

- `openspec/specs/` — актуальные функциональные и системные требования, наблюдаемое поведение
  и проверяемые сценарии.
- `ARCHITECTURE.md` — архитектурные границы, принципы, технические ограничения, взаимодействия
  компонентов и фактическая реализация этих принципов.
- `protos/` — исходные контракты gRPC/REST и сообщений Rebus.
- `README.md` — обзор репозитория, команды сборки, запуска и настройки.
- `AGENTS.md` — правила разработки и обязательного документирования контрактов.
- `openspec/changes/archive/` — история изменений, включая исходные причины, проектирование и
  задачи реализации.

OpenSpec Specs описывают, **что система должна делать**. Этот документ описывает, **какие
архитектурные границы и механизмы приняты и как система устроена сейчас**. Детальные сценарии
возможностей не дублируются здесь, если они уже описаны в соответствующих Specs.

**Актуальность.** Фактическое состояние кода проверяется и обновляется после архивации OpenSpec
Change, если изменение затронуло компоненты, структуру, взаимодействия, контракты или архитектурные
ограничения. История изменений не копируется в этот файл: её источником является OpenSpec Archive.

## 0. Архитектурный контекст и принципы

### 0.1. Назначение и границы ответственности

`LtiIntegrationGateway` — самостоятельное приложение, обеспечивающее интеграцию продуктов LTI с
внешними системами по HTTP API. Шлюз централизует транспортные механизмы интеграции, каналы,
аутентификацию, валидацию входящих контрактов, маршрутизацию сообщений, повторные попытки,
сохранение результатов и аудит.

**Продукты владеют бизнес-смыслом интеграции.** Продукт выбирает бизнес-операцию, знает структуру
внешнего API, формирует данные исходящего запроса и интерпретирует ответ внешней системы. Шлюз не
выполняет бизнес-маппинг и не принимает бизнес-решения на основании содержимого HTTP-ответа.

Граница взаимодействия:

- бизнес-данные и результаты интеграционных операций между продуктами и шлюзом передаются через
  Rebus;
- продукты не используют REST/gRPC управляющего API для отправки бизнес-запросов или получения
  результатов интеграции;
- gRPC и REST (через JSON transcoding) используются для управления каналами, маршрутами,
  контрактами и конфигурацией, а также для диагностических операций;
- входящие HTTP-вызовы от внешних систем принимаются шлюзом и передаются целевому продукту через
  Rebus.

Исходящий и входящий контуры независимы и имеют раздельные модели каналов: `OutboundChannel` и
`InboundChannel`. Единый двунаправленный канал не используется.

### 0.2. Контекст взаимодействия

```mermaid
flowchart TB
    P[Продукты LTI]
    R[Rebus]
    subgraph G[LtiIntegrationGateway]
        O[Outbound Dispatcher]
        I[Inbound HTTP endpoints]
        V[OpenAPI validation]
        M[Management API]
        C[Channel and contract registry]
        A[Authorization and secret access]
        L[Message logs and audit]
    end
    DB[(Configuration and message storage)]
    E[External systems]

    P -->|OutboundRequest| R
    R --> O
    O --> C
    O --> A
    O -->|HTTP request| E
    E -->|HTTP response| O
    O --> L
    O -->|OutboundResult| R
    R --> P

    E -->|HTTP callback| I
    I --> V
    V --> L
    V -->|InboundMessage| R
    R --> P

    M --> C
    C <--> DB
    L <--> DB
```

### 0.3. Стиль приложения и организация кода

Приложение реализовано как модульный монолит с разделением по функциональным областям:

- весь производственный код находится в `LtiIntegrationGateway`;
- автоматические тесты находятся в отдельном `LtiIntegrationGateway.Tests`;
- функциональность организована по Feature-first и Vertical Slice Architecture;
- вертикальный срез содержит сценарий и необходимые ему проверки, работу с данными и результат;
- общие инфраструктурные компоненты располагаются в `Infrastructure`;
- универсальные репозитории, медиаторы и абстракции не вводятся без конкретной необходимости;
- производственный код шлюза не зависит от сборок конкретных продуктов.

### 0.4. Контрактный подход и совместимость

Protobuf используется как contract-first формат для gRPC API и сообщений, передаваемых через Rebus.
Файлы `.proto` являются исходным определением транспортных контрактов; C#-типы генерируются
через `Grpc.Tools` и не поддерживаются вручную как параллельные DTO.

Контракты не зависят от EF Core-сущностей, внутренней модели хранения или бизнес-моделей продуктов.
Опубликованные `.proto` распространяются как исходные файлы из репозитория; потребители фиксируют
используемую версию по тегу или коммиту.

Правила совместимости:

- внутри версии допускаются совместимые добавочные изменения;
- несовместимое изменение оформляется новой версией protobuf package, а шлюз на переходном этапе
  может обрабатывать несколько версий параллельно;
- номер поля не переиспользуется; удалённые номера и имена помечаются `reserved`;
- смысл существующего поля не меняется в рамках той же версии;
- нулевое значение enum именуется `*_UNSPECIFIED`; для скалярных полей, где важно различать
  отсутствие и значение по умолчанию, используется `optional`;
- `google.protobuf.Any` не используется в интеграционных сообщениях; тела запросов и ответов
  передаются как `bytes`.

Сериализация сообщений Rebus использует ProtoJSON (UTF-8) через собственный `ISerializer`:

| Элемент | Правило |
|---|---|
| Формат тела | ProtoJSON, UTF-8 |
| Печать | `JsonFormatter.Default`; имена camelCase, enum — строковыми именами, `bytes` — base64 |
| Разбор | `JsonParser` с `IgnoreUnknownFields = true`; поддерживаются camelCase и snake_case |
| Неизвестное имя enum | ошибка разбора; неизвестное числовое значение enum сохраняется |
| 64-битные целые | строковое представление по правилам ProtoJSON |
| `rbs2-msg-type` | полное protobuf-имя сообщения |
| `rbs2-content-type` | `application/json` |

### 0.5. Статические и динамические HTTP endpoint

Управляющие gRPC-сервисы публикуются также как REST endpoint через JSON transcoding. Эти маршруты
определены статическими Protobuf-контрактами приложения.

Inbound endpoint-ы формируются динамически из опубликованных OpenAPI-контрактов. Для них используется
кастомный `EndpointDataSource`, подключённый к `IEndpointRouteBuilder.DataSources`. При активации
ревизии каталог маршрутов обновляется без пересборки и перезапуска приложения.

Публикация набора маршрутов отклоняется, если в активной конфигурации есть конфликтующие пары
«HTTP-метод + шаблон». Пересекающиеся, но не конфликтующие шаблоны сопоставляются штатным механизмом
приоритета ASP.NET Core. Поведение динамического добавления/удаления маршрутов и применения rate limit
покрыто интеграционными тестами.

JSON transcoding не используется для публикации динамических маршрутов из загруженного OpenAPI:
это отдельный runtime-механизм ASP.NET Core.

Контракт управляющего API публикуется и как OpenAPI-документ: `/openapi/management.json` и Swagger UI
(`/swagger`). Документ строится при старте из встроенного дескриптора `management_api.proto`: пути и
HTTP-методы берутся из аннотаций `google.api.http`, схемы — из сообщений, описания — из комментариев
`.proto`. Отдельного источника истины для документа нет, поэтому он не расходится с gRPC-контрактом.

### 0.6. Жизненный цикл конфигурации

Каналы и OpenAPI-контракты изменяются через версионируемые конфигурации:

1. создаётся или изменяется черновик;
2. конфигурация проверяется;
3. валидная конфигурация публикуется как неизменяемая ревизия;
4. ревизия активируется;
5. при необходимости выполняется откат к предыдущей ревизии.

Runtime использует согласованные снимки конфигурации. Запрос обрабатывается в рамках той ревизии,
с которой он был начат. Изменения каналов, контрактов и маршрутов применяются без перезапуска.
Изменение секретов, поступающих через переменные окружения, требует перезапуска процесса, поскольку
источники конфигурации .NET не перечитываются автоматически.

### 0.7. Безопасность и надёжность: архитектурные ограничения

- Значения секретов не хранятся в БД и не передаются в сообщениях Rebus, результатах API или логах.
  В конфигурации хранятся тип авторизации, ключ конфигурации и несекретные параметры; секреты
  читаются через `ISecretStore` из конфигурации окружения.
- Проверка отсутствующих секретов активной конфигурации выполняется при старте приложения; отсутствие
  необходимого секрета приводит к fail-fast отказу запуска.
- Для исходящих HTTP-вызовов базовый URL берётся из конфигурации канала; путь запроса является
  относительным и структурно проверяется до отправки. Автоматические редиректы отключены.
- Проверка серверного TLS-сертификата не отключается; минимальная версия TLS — 1.2.
- Клиентские сертификаты учитываются при выделении HTTP handler pool: соединения не разделяются
  между конфигурациями с разными сертификатами.
- HTTP-повторы определяются транспортной ошибкой и HTTP-статусом, а не бизнес-содержимым тела
  ответа. Для потенциально небезопасных `POST`/`PATCH` повтор допустим только при наличии
  `idempotency_key`.
- Гарантия доставки результата — «минимум один раз», а не exactly-once. Получатель должен
  учитывать возможную повторную доставку и выполнять дедупликацию по `message_id`.
- Исходящий результат сохраняется до попытки отправки и может быть повторно доставлен фоновым
  диспетчером.
- Текущий транспорт Rebus — in-memory: сообщения в очереди не переживают остановку или аварию
  процесса. Такой транспорт подходит для начального этапа и тестирования, но не обеспечивает
  промышленную надёжную доставку. Для входящего контура гарантия доставки также зависит от
  настроенного транспорта Rebus.

### 0.8. Поддержка OpenAPI

Для входящих контрактов используется целевой формат OpenAPI 3.0. Документы Swagger 2.0
конвертируются при загрузке; OpenAPI 3.1 не принимается текущей реализацией.

Проверка документа выполняется при публикации и работает по принципу fail-closed:

- поддерживается закрытый перечень конструкций OpenAPI 3.0;
- разрешены только локальные `$ref`; внешние HTTP(S), `file`, protocol-relative и относительные
  ссылки запрещены;
- `$ref` с соседними полями отклоняется;
- конфликты маршрутов и проблемы контракта обнаруживаются до активации;
- схемы запросов компилируются для активной ревизии и используются повторно при обработке запросов.

Поддерживаемые конструкции и конкретные правила валидации отражаются в OpenSpec spec
`contract-registry` и связанных спеках входящего контура.

## 1. Состав решения

| Элемент | Назначение |
|---|---|
| `LtiIntegrationGateway.slnx` | решение; все проекты подключены |
| `src/LtiIntegrationGateway.Contracts` | генерация C# из `protos/`; контракты компилируются один раз; дескриптор управляющего контракта с комментариями встраивается в сборку |
| `src/LtiIntegrationGateway.Core` | модель данных, `GatewayDbContext`, миграции, чтение и проверка OpenAPI, построение и проверка наборов маршрутов, `ISecretStore`, `Result<T>` |
| `src/LtiIntegrationGateway.Management` | управляющий gRPC/REST API, OpenAPI-документ и Swagger UI, жизненный цикл конфигурации, аудит, единственный владелец схемы и миграций |
| `src/LtiIntegrationGateway.Flow` | входящий и исходящий контуры, транспорт Rebus, каталог маршрутов, применение конфигурации |
| `src/LtiIntegrationGateway.ServiceDefaults` | OTel, пробы `/health/live` и `/health/ready`, настройка хранилища |
| `src/LtiIntegrationGateway.AppHost` | Aspire: PostgreSQL, реплики management и flow, dashboard с телеметрией и состоянием проб готовности |
| `tests/LtiIntegrationGateway.Core.Tests` | тесты модели, OpenAPI, маршрутов и сериализации |
| `tests/LtiIntegrationGateway.Management.Tests` | тесты управляющего API, жизненного цикла и изоляции интеграций |
| `tests/LtiIntegrationGateway.Flow.Tests` | тесты входящего и исходящего контуров, применения конфигурации |
| `tests/LtiIntegrationGateway.TestSupport` | общая обвязка: PostgreSQL в контейнере, хосты сервисов, фикстуры |

Ключевые пакеты: `Google.Protobuf`, `Grpc.Tools` (генерация из `.proto`), `Grpc.AspNetCore` +
`Microsoft.AspNetCore.Grpc.JsonTranscoding` (gRPC и REST из одного контракта),
`Swashbuckle.AspNetCore.SwaggerUI` (только UI, без зависимости от `Microsoft.OpenApi`),
`Npgsql.EntityFrameworkCore.PostgreSQL` + `Microsoft.EntityFrameworkCore`,
`Microsoft.OpenApi` и `NJsonSchema` (чтение и проверка контрактов), `Rebus` + `Rebus.PostgreSql` +
`Rebus.ServiceProvider`, `Testcontainers.PostgreSql` (тесты). Версии задаются в
`Directory.Packages.props`.

## 2. Точки сборки приложения

Композиция разделена на два хоста; общая часть — `AddGatewayStorage`, `AddGatewayObservability` и пробы.

| Хост | Что регистрирует | Чего не делает |
|---|---|---|
| `Management/Program.cs` | хранилище, OTel (`service.name = management`), health, управляющий gRPC-фасад, срезы конфигурации, аудит, `AddGrpc().AddJsonTranscoding()`, `Database.Migrate()`, `/openapi/management.json` и Swagger UI `/swagger` | не подключает Rebus, не применяет конфигурацию в runtime, не регистрирует входящие маршруты |
| `Flow/Program.cs` | хранилище, OTel (`service.name = flow`), health, `FlowOptions`, Rebus, входящий и исходящий контуры, `FlowConfigurationApplier`, ограничение частоты, источник динамических endpoint'ов | не применяет миграции, не публикует управляющий API |

Оба хоста проверяют конфигурацию **после** `builder.Build()`: тестовые переопределения видны только к
этому моменту. Отсутствие `ConnectionStrings:Gateway` завершает запуск исключением с именем ключа.

`Management` применяет миграции при старте; реплики стартуют последовательно. `Flow` миграции не
применяет: при несовместимой схеме запросы завершаются ошибкой, схема не изменяется (spec
`gateway-host`).

`Flow` без `Gateway:Integration` не запускается: имя интеграции определяет входную очередь,
error-queue и область действия проверок.

`AddGatewayObservability` читает `Observability:Otel`, создаёт OTLP-экспортёры traces/metrics и
регистрирует `GatewayMetrics`; имя сервиса по умолчанию задаётся хостом (`management` или `flow`), а
переменная `OTEL_SERVICE_NAME` имеет приоритет. `Flow` дополнительно фиксирует интеграцию процесса в
`GatewayTelemetry`, поэтому все бизнес-трассы и метрики содержат измерение `integration`.

## 3. Карта каталогов

| Каталог | Содержимое | Где описано |
|---|---|---|
| `protos/v1` | интеграционные сообщения `lti.gateway.messages.v1` и управляющий API `lti.gateway.management.v1`; каталог в корне репозитория | контракты `protos/v1` и OpenSpec Specs |
| `protos/google/api` | вендорные `annotations.proto` и `http.proto`; не редактируются, C#-типы берутся из `Google.Api.CommonProtos` | раздел 0.5 |
| `Core/Infrastructure/Persistence` | `GatewayDbContext`, сущности по логическим папкам, миграции, `IntegrationRegistry` | раздел 5 |
| `Core/Infrastructure/OpenApi` | чтение документа, fail-closed проверки, извлечение и кэширование схем тела | раздел 0.8 |
| `Core/Infrastructure/Routing` | каталог наборов маршрутов, валидатор набора, метаданные маршрута, лимит частоты | раздел 0.5 |
| `Core/Infrastructure/Security` | `ISecretStore`, `IChannelCertificateStore` и реализации поверх `IConfiguration` | раздел 0.7 |
| `Core/Infrastructure/Results` | `Result<T>` для операций с отказом | раздел 0.3 |
| `Core/Features/Contracts` | `ContractVersionSettings`, `ContractOperationSettings`, `ContractRouteSets` | spec `contract-registry` |
| `Core/Features/Host` | `StartupSecretValidator` с фильтром по интеграции | spec `gateway-host` |
| `Management/Features/Channels` | жизненный цикл outbound-канала и чтение состояния | spec `channel-registry` |
| `Management/Features/InboundChannels` | создание и настройки inbound-канала | spec `channel-registry` |
| `Management/Features/Contracts` | загрузка, публикация, активация версий и настройки операций | spec `contract-registry` |
| `Management/Features/ProductRoutes` | маршруты продуктов: разрешённые каналы и вычисленный адрес `<product_id>.inbox` | spec `outbound-dispatch` |
| `Management/Features/Management` | gRPC-фасад управляющего API и маппинг proto ↔ домен | spec `management-api` |
| `Management/Infrastructure/Management` | ключ управляющего API и аудит управляющих операций | раздел 8 |
| `Management/Infrastructure/Documentation` | OpenAPI-документ управляющего API из дескриптора контракта | `/openapi/management.json`, Swagger UI `/swagger` (раздел 0.5) |
| `Flow/Features/Configuration` | `FlowOptions`, `FlowConfigurationApplier`: сверка версии, применение наборов, отчёт | spec `configuration-propagation` |
| `Flow/Features/Outbound` | обработка `OutboundRequest`: проверки, авторизация, HTTP с повторами, операция и журнал, диспетчер доставки | spec `outbound-dispatch` |
| `Flow/Features/Inbound` | приём входящего вызова: проверки, аутентификация, публикация `InboundMessage`, журнал, отказ отключённой операции | spec `inbound-dispatch`, `inbound-routing` |
| `Flow/Infrastructure/Http` | пул соединений по ключу канала | раздел 0.7 |
| `Flow/Infrastructure/Rebus` | сериализатор ProtoJSON, реестр типов, конфигурация шины | разделы 0.4 и 6 |
| `Flow/Infrastructure/Routing` | источник endpoint'ов из каталога маршрутов | раздел 0.5 |
| `Flow/Infrastructure/Observability` | backlog-gauge ожидающих доставки результатов | раздел 8 |
| `ServiceDefaults/Infrastructure` | OTel, пробы, `AddGatewayStorage`, `DatabaseHealthCheck` | разделы 2 и 8 |
| `AppHost` | Aspire-контур разработки: PostgreSQL, два management, по два flow на интеграцию, состояние проб готовности в dashboard | README |

`bin`, `obj`, `Properties` — служебные, в архитектуре не участвуют.

## 4. Вертикальные срезы

Каждый сценарий — отдельный каталог с командой/запросом, результатом и обработчиком; общих
репозиториев и медиатора нет (раздел 0.3).

### Каналы (spec `channel-registry`)

```text
Features/Channels
├── CreateChannel            CreateChannelHandler
├── UpdateChannelDraft       UpdateChannelDraftHandler
├── PublishChannelRevision   PublishChannelRevisionHandler
├── ActivateChannelRevision  ActivateChannelRevisionHandler
├── RollbackChannelRevision  RollbackChannelRevisionHandler
├── GetChannel               GetChannelHandler
├── GetChannelHealth         GetChannelHealthHandler: активная ревизия, секрет, сертификат, ошибки, версии экземпляров
├── ChannelConfiguration      настройки ревизии (record)
├── ChannelConfigurationValidator  проверки публикации
├── ChannelRevisionWriter     перенос настроек в сущность и обратно
└── ChannelAuditWriter        аудит изменения в транзакции среза
```

### Маршруты продуктов и исходящий контур (spec `outbound-dispatch`)

```text
Features/ProductRoutes
├── RegisterProductRoute     upsert каналов и вычисленного адреса `<product_id>.inbox`
└── GetProductRoute          чтение маршрута: адрес всегда вычисленный

Features/Outbound
├── OutboundRequestHandler        адаптер Rebus: заголовки → команда
├── ProcessOutboundRequestHandler сценарий: порядок проверок и вызов
├── ResolveSenderIdentityHandler         product_id + rbs2-sender-address по ProductRoutes
├── LoadActiveChannelHandler             активная ревизия канала
├── RelativePathValidator                структурная проверка пути без allow-list
├── ResolveAuthorizationHandler          none / bearer / api_key / basic / client_certificate
├── ExecuteHttpCallHandler               HTTP с повторами по политике канала, обработчик из пула
├── OutboundOperationRecorder     операция и журнал одной транзакцией
├── StoredOutboundResultFactory   восстановление результата из операции и журнала
├── PublishOutboundResultHandler         Send результата и признак доставки
├── OutboundResultDispatcher      фоновая повторная доставка `pending`-записей
├── OutboundDeliveryOptions       параметры диспетчера (включение, интервал, пачка)
└── OutboundErrorCodes            коды отказов и транспортных ошибок

Infrastructure/Http
├── ChannelHttpClientKey      базовый адрес, тип авторизации, отпечаток сертификата
├── ChannelHttpClientPool     SocketsHttpHandler по ключу, TLS 1.2+, без редиректов
└── ChannelHttpClientLease    аренда обработчика на время вызова
```

### Контракты (spec `contract-registry`, `management-api`)

```text
Features/Contracts
├── UploadContractVersion          документ → черновик версии
├── PublishContractVersion         операции, схемы тела, дубли operationId, конфликты, очередь, перенос настроек
├── ActivateContractVersion        фиксация желаемого состояния: проверки по данным БД, транзакция, conflict
├── ListContractOperations         операции версии с настройками
├── GetContractVersion             версия с настройками и операциями
├── UpdateContractVersionSettings  настройки приёма черновика
├── UpdateContractOperationSettings настройки операции: enabled, очередь; проверяются по данным БД
├── ContractVersionSettings        настройки приёма версии (record)
├── ContractOperationSettings      действующие настройки операции: очередь — операция → версия; квота — версия → приложение (record)
└── ContractRouteSets              InboundRouteSet из операций версии с действующими настройками
```

### Хост (spec `gateway-host`)

```text
Features/Host
└── StartupSecretValidator  проверка секретов активных ревизий каналов и версий контрактов при старте
```

### Входящий контур (spec `inbound-dispatch`)

```text
Features/Inbound
├── InboundRequestHandler          проверки, InboundMessage, журнал, 202 async / ответ sync / 504 / 502
├── InboundRequestPath             фактический путь запроса без query и декодирования
├── InboundRequestAuthenticator    none, api_key, basic, client_certificate
├── InboundDisabledOperationHandler 404 без чтения тела и аутентификации + аудит InboundOperationDisabled
├── InboundMessageLogWriter        запись Pending → Sent | Replied | TimedOut | Cancelled | Failed
└── InboundMessagePublisher        IInboundMessagePublisher + Rebus Send / SendRequest

Infrastructure/Routing
├── InboundRouteDefinition     маршрут, настройки операции, режим ответа и timeout
├── InboundRouteCatalog        активные наборы, снапшот, токен изменения
├── InboundRouteSetValidator   методы, шаблоны, конфликты (включая отключённые операции)
├── InboundEndpointDataSource  RouteEndpoint'ы, метаданные маршрута, делегат приёма или отказа
├── InboundChannelMetadata     канал как ключ лимита частоты
├── InboundRouteMetadata       ревизия, операция, очередь, авторизация, квота, mode/timeout
└── InboundRateLimitOptions    квота и окно по умолчанию

Infrastructure/OpenApi
├── InboundSchemaCache         компиляция схем на ревизию, удаление при деактивации
└── OpenApiDocumentValidator   извлечение схем тела с инлайнингом локальных $ref
```

### Управляющий API (spec `management-api`)

```text
Features/Management
├── ManagementApiService              gRPC-фасад: зависимости и общие помощники
├── ManagementApiService.Channels     outbound-каналы и ревизии
├── ManagementApiService.InboundChannels  inbound-каналы, настройки и health
├── ManagementApiService.Catalog      маршруты продуктов, статус операции
├── ManagementApiService.Contracts    версии контрактов inbound-каналов
└── ChannelMapping                    proto ↔ ChannelConfiguration

Infrastructure/Documentation
└── ManagementOpenApiDocument         OpenAPI 3.0 из дескриптора management_api.proto
```

Фасад не содержит доменных правил: он вызывает срезы, отображает результаты в proto-сообщения,
возвращает `NotFound` для отсутствующих объектов и пишет аудит управляющих операций.

## 5. Инфраструктурные компоненты

| Компонент | Ответственность | Особенности |
|---|---|---|
| `Persistence/GatewayDbContext` | модель данных и конфигурация EF Core | уникальные индексы, каскады, конвертация enum и политики повторов в строку/JSON |
| `Persistence/Entities` | outbound/inbound-каналы, ревизии, маршруты продуктов, контракты с настройками приёма, операции со схемами, журналы; папки по логическим областям, все классы реализуют `IEntity`, публичные свойства документированы | опубликованные ревизии и версии контрактов неизменяемы; направления разделены |
| `Persistence/Migrations` | `InitialSchema`, `IntegrationScope`, `IntegrationRuntimeScope` — схема PostgreSQL с интеграцией и runtime-полями | применяются только `Management`; шаги expand/contract-совместимы |
| `Rebus/ProtobufSerializer` | `ISerializer` на ProtoJSON | заголовки `rbs2-msg-type` и `rbs2-content-type` |
| `Rebus/ProtobufMessageRegistry` | реестр типов сообщений | неизвестный тип — диагностируемая ошибка |
| `Rebus/RebusConfigurationExtensions` | регистрация шины, error-queue и обработчика | входная очередь `lti.gateway.<integration>.outbound`, error-queue `lti.gateway.<integration>.error` |
| `OpenApi/MicrosoftOpenApiDocumentReader` | чтение документа, версия, синтаксис | `Microsoft.OpenApi` 3.11.0 (раздел 0.8) |
| `OpenApi/OpenApiDocumentValidator` | fail-closed проверки, извлечение операций и схем тела | запрещённые ключевые слова, `$ref`, `format`; инлайнинг `$ref` до глубины 32 |
| `OpenApi/InboundSchemaCache` | скомпилированные схемы активных ревизий | `NJsonSchema` 11.6.1, компиляция при активации |
| `Management/ManagementApiKeyMiddleware` | ключ управляющего API | `401`/`503`, gRPC `Unauthenticated` |
| `Management/ManagementAuditWriter` | аудит управляющих операций, включая чтение и отказы `NotFound` | без значений секретов; усечение объекта и деталей до длин модели |
| `Features/Host/StartupSecretValidator` | fail-fast проверка секретов активной конфигурации после миграций | активные ревизии каналов и версии контрактов; сообщения содержат только имена ключей |
| `Security/ConfigurationSecretStore` | значение секрета по ключу | только `IConfiguration` |
| `Security/ConfigurationChannelCertificateStore` | клиентский сертификат по `config_key` | PFX из файла или base64, пароль `<config_key>:Password`, кэш на время жизни процесса |
| `Http/ChannelHttpClientPool` | пул `SocketsHttpHandler` по ключу канала | TLS 1.2+, без редиректов, проверка серверного сертификата, срок жизни соединения 5 минут |

Имена очередей сосредоточены в `Rebus/QueueNames` (имена очередей определены в разделе 6): входная очередь
`lti.gateway.<integration>.outbound`, error-queue `lti.gateway.<integration>.error` (настраивается
`RetryStrategy` с сохранением значений по умолчанию для числа попыток и кулдауна), reply-очередь
экземпляра `lti.gateway.<integration>.reply.<instance_id>`, очередь продукта вычисляется как
`<product_id>.inbox`. Имя интеграции обязано соответствовать
`^[a-z][a-z0-9-]{1,31}$`; адрес с другим именем отклоняется. Список `ProductRouteQueueAddress`
остаётся в модели для обратной совместимости чтения, но проверки `rbs2-sender-address` и
`rbs2-return-address` идут против вычисленного адреса; регистрация маршрута принимает пустой список
или вычисленный адрес.

## 6. Контракты и генерация кода

- `protos/v1/integration_messages.proto` — сообщения Rebus (`lti.gateway.messages.v1`):
  `OutboundRequest`, `OutboundResult`, `InboundMessage`, `InboundResponse`, `InboundResponseHeader`,
  `QueryParameter`, `Outcome`. `reply_expected` включает request/reply для sync inbound.
- `protos/v1/management_api.proto` — управляющий API (`lti.gateway.management.v1`): ресурсы
  `/v1/integrations/{integration}/outbound-channels`, `/inbound-channels`, `/products/{product_id}/route`,
  `/operations/{message_id}`; состояние отдают RPC `GetOutboundChannelHealth` и
  `GetInboundChannelHealth`, включая действующую версию конфигурации и applied-версии экземпляров.
  Используются `google.protobuf.Timestamp`.
- `protos/google/api/*.proto` — вендорные дескрипторы; подключаются только как путь импорта,
  C#-типы берутся из `Google.Api.CommonProtos` (иначе конфликт типов CS0436).
- Генерация выполняется при сборке (`Grpc.Tools`) в проекте `LtiIntegrationGateway.Contracts`;
  остальные проекты ссылаются на него и не компилируют контракты повторно. Сгенерированные файлы
  не редактируются и в репозитории не хранятся.
- Обязательное требование к документации контрактов — `AGENTS.md`.

## 7. Потоки и транзакционные границы

**Outbound.** `OutboundRequest` → проверки (поля, продукт и вычисленный адрес отправителя
`<product_id>.inbox`, тот же адрес для доставки результата, идемпотентность, активная ревизия канала
своей интеграции, структурная проверка пути, авторизация) → операция `Accepted` → `InProgress` →
HTTP-вызов с повторами →
**одна транзакция**: операция `Completed` + запись журнала
`pending` (с адресом доставки, `correlation_id`, текстом ошибки, номером ревизии канала и
`CreatedAt`) → `Send` результата → журнал `sent`. Отказы, возникшие до проверки адреса возврата,
не доставляются: операция `Completed`, журнал `undeliverable`, адрес пуст. Невалидный DTO
(`FailFastException`) уходит в `lti.gateway.<integration>.error` без операции, журнала и HTTP-вызова.
Маршрут продукта, канал и операция ищутся только в пространстве интеграции экземпляра; канал с тем же
именем в другой интеграции даёт `CHANNEL_NOT_ALLOWED`, а одинаковый `message_id` в другой интеграции —
независимая операция (spec `outbound-dispatch`, `integration-scope`).

Недоставленные записи `pending` повторно отправляет `OutboundResultDispatcher`: выборка идёт в SQL по
индексу `(IntegrationId, DeliveryStatus, CreatedAt)` с фильтром «интеграция экземпляра, адрес не пуст
и операция существует», поэтому недоставляемые и чужие записи не занимают пачку; отправка и фиксация
статуса обрабатываются по отдельности, а гарантия остаётся «минимум один раз». Повторный
`OutboundRequest` с завершённым `message_id` отдаёт идентичный сохранённый результат на адрес из
текущего сообщения; владение `product_id` проверяется до выдачи результата, а проверки канала и
авторизации для завершённой операции не повторяются (см. разделы 0.7 и 7; spec `outbound-dispatch`).

**Контракты.** Загрузка создаёт черновик версии; публикация извлекает операции и схемы тела
(локальные `$ref` разворачиваются), требует очередь получателя в настройках версии формата
`<product_id>.inbox` (см. разделы 0.4 и 6),
дополнительно проверяет действующую очередь каждой включённой операции, переносит настройки операций
из предыдущей версии канала (по `operationId`, а при его отсутствии хотя бы у одной из операций — по
совпадению метода и шаблона; изменённый `operationId` при том же маршруте — новая операция) и
проверяет конфликты только внутри интеграции.

**Активация — желаемое состояние.** Активация версии контракта или ревизии канала выполняется
`Management` и означает «зафиксировано и прошло проверки»: конфликты маршрутов проверяются по данным
БД, изменение фиксируется одной транзакцией вместе с ростом `Integration.ConfigurationVersion`, а
ответ отправляется после коммита. Runtime-состояние сервиса управления не меняется: каталог
`InboundRouteCatalog` и кэш схем принадлежат `Flow`. Гонку двух реплик разрешает частичный уникальный
индекс: проигравшая транзакция получает нарушение уникальности и возвращает проблему `conflict`
(spec `management-api`, `contract-registry`).

**Применение конфигурации.** Каждый экземпляр `Flow` раз в `Gateway:PollIntervalSeconds` (по
умолчанию 2 с) сравнивает `ConfigurationVersion` интеграции со своей applied-версией. При расхождении
он строит наборы маршрутов всех активных версий контрактов своей интеграции, атомарно заменяет ими
свои наборы, прогревает кэш схем и записывает в `FlowInstanceStates` применённую версию, состояние
секретов, срок сертификата и отсутствие ошибки. Если проверка набора не прошла, каталог и applied-версия
не меняются: продолжает обслуживаться предыдущая конфигурация, а текст ошибки попадает в
`FlowInstanceStates.ApplyError`. Отчёт публикуется и без изменения версии, поэтому `ReportedAt`
остаётся актуальным. Management читает эти строки для диагностики; признак устаревания — отчёт старше
15 секунд (spec `configuration-propagation`).

**Настройки операции.** Действующие значения вычисляются при построении набора маршрутов
(`ContractRouteSets`): очередь — операция, иначе версия; квота и окно — только версия, иначе
`Inbound:RateLimit:*`; авторизация берётся только из версии. Все операции канала делят один
бюджет ограничения частоты в пределах экземпляра. Операции извлекаются при публикации,
поэтому для черновика настройки операции недоступны (`404`). Изменение настроек активной версии
проверяется по данным БД и увеличивает версию конфигурации; применение выполняют экземпляры `Flow`;
версия `Superseded` отклоняется с проблемой `version` (spec `contract-registry` и `management-api`).

**Входящий контур.** Запрос попадает на динамический endpoint с метаданными маршрута, далее
фиксированный порядок: query (≤ 64) → `Content-Type` по схемам ревизии → наличие схемы тела →
аутентификация по настройкам ревизии → глубина JSON (≤ 64) → валидация по схеме → запись журнала
`pending` → `Send` в очередь получателя → журнал `sent` и `202`. Любой отказ пишется в журнал
`failed` с кодом ошибки; при сбое `Send` ответ `503` (spec `inbound-dispatch`).
Отключённая операция обслуживается отдельным делегатом: тело не читается, аутентификация не
выполняется, в `ManagementAuditEntry` пишется `InboundOperationDisabled`, ответ — `404`.

В `SYNC` режиме экземпляр регистрирует ожидание в `PendingInboundRequests` по корреляции запроса и
отправляет `InboundMessage` с заголовками `rbs2-return-address` (reply-очередь экземпляра) и
`rbs2-corr-id`. Ответ продукта принимает reply-шина (`FlowReplyBusHost`), поэтому при нескольких
репликах его получает принявший вызов экземпляр. Несовпадающий `message_id`, недопустимый статус или
заголовок отклоняются как `502`; таймаут и отмена завершают ожидание через `WaitAsync`, поздний ответ
ожидания не находит и журнал не меняет.

**Каналы.** Ревизия — агрегат настроек: правки идут в черновик, публикация фиксирует неизменяемую
ревизию, активация делает её действующей, откат возвращает предыдущую (spec `channel-registry`).

**Аудит.** `ChannelAuditEntry` пишется в транзакции доменного среза; `ManagementAuditEntry`
фиксирует факт управляющего вызова, включая отклонённые и читающие операции.

## 8. Соглашения и повторяемые решения

| Решение | Причина |
|---|---|
| Ревизия — агрегат настроек, в `OutboundChannel`/`InboundChannel` настроек нет | неизменяемость опубликованного и безопасный откат |
| Результат операции — запись с `Problems`, а не исключение | отказ домена не является сбоем, список проблем возвращается целиком |
| Операции с одним значением и возможным отказом возвращают `Result<T>` | единая форма `Value`/`ErrorCode`/`ErrorMessage` вместо дублирующихся result-записей |
| Новые дочерние сущности добавляются через `DbSet`, а не только в навигацию | клиентский Guid-ключ: EF считает их `Modified`, `UPDATE` не находит строку |
| Моменты времени хранятся как `timestamptz` без EF-конвертера | PostgreSQL сравнивает и индексирует `DateTimeOffset` корректно, поэтому Unix-миллисекунды не нужны |
| Строковые поля proto заполняются только непустыми значениями | присваивание `null` бросает `ArgumentNullException` |
| Динамические endpoint'ы добавляются в `IEndpointRouteBuilder.DataSources` | регистрации `EndpointDataSource` в DI недостаточно (см. раздел 0.5) |
| Управляющий gRPC-фасад публикуется только в `Management` | `Flow` не содержит управляющего API; несовпадающий метод маршрута `Flow` получает `405` |
| Секреты читаются только через `ISecretStore` | в базе и API хранятся лишь ключи конфигурации |
| Схемы тела компилируются один раз на ревизию | валидация не тратит CPU на каждый запрос (раздел 0.8) |
| Публикация сообщения идёт через `IInboundMessagePublisher` | транспорт изолирован, сбой передачи наблюдаем в тестах |
| Запись журнала `pending` создаётся до `Send` | сбой передачи виден в журнале, а не теряется |
| Сравнение секретов — `CryptographicOperations.FixedTimeEquals` | исключение утечки по времени (см. раздел 0.7) |

## 9. Тесты

Тесты разделены по сервисам; общая обвязка — `TestSupport`.

| Проект | Что проверяется |
|---|---|
| `Core.Tests` | миграции и инварианты схемы, OpenAPI, `MessageContractValidator`, сериализация и round-trip ProtoJSON, health-проверка |
| `Management.Tests` | жизненный цикл каналов и версий контрактов, управляющий API и аудит, изоляция интеграций, гонки активаций, состояние канала с applied-версиями |
| `Flow.Tests` | маршрутизация, приём и исходящий вызов, идемпотентность, диспетчер, изоляция интеграций, применение конфигурации, перезапуск и две реплики |

`TestSupport`:

- `PostgresTestDatabase` — `Testcontainers.PostgreSql`: контейнер на прогон, база-клон шаблона на тест,
  реальные частичные уникальные индексы и `FOR UPDATE SKIP LOCKED`;
- `GatewayHostFactoryBase`, `ManagementHostFactory`, `FlowHostFactory` — хосты с подменой строки
  подключения и дополнительными ключами конфигурации;
- `GatewayPair` — пара хостов над одной базой: конфигурация задаётся через управление, проверки — через
  `Flow`;
- `OutboundFixture` — PostgreSQL, тестовый HTTP-обработчик и два in-memory endpoint'а Rebus; собирает
  обработчик и диспетчер так же, как приложение;
- `InboundFixture` — PostgreSQL, тестовые секреты, `DefaultHttpContext` с метаданными маршрута и два
  in-memory endpoint'а Rebus (шлюз и продукт);
- `ManagementApi` — REST-клиент с ключом и in-process gRPC-канал;
- `RecordingBus`, `OpenApiSamples`, `RecordingHttpMessageHandler`, `ContractJson`, `TelemetryCollector`,
  `InMemoryLogger*`.

Запуск: `dotnet test LtiIntegrationGateway.slnx` (через `Microsoft.Testing.Platform`, см. `global.json`).
Для тестов требуется Docker: PostgreSQL поднимается контейнером.

## 10. Поддержание актуальности

После реализации и архивации OpenSpec Change:

1. проверить, что постоянные Specs отражают актуальные функциональные требования и сценарии;
2. обновить этот документ, если изменились архитектурные границы, компоненты, контракты,
   взаимодействия, транзакционные границы, инфраструктурные ограничения или фактическая структура кода;
3. описывать здесь текущее состояние, а не копировать историю принятия решения или перечень изменений;
4. удалить описания компонентов, путей и механизмов, которые больше не существуют;
5. проверить ссылки на Specs, `.proto`, README и AGENTS.md.

OpenSpec Change и его архив содержат контекст изменения, рассмотренные варианты, задачи и историю.
`ARCHITECTURE.md` не ведёт собственный журнал версий и не заменяет OpenSpec Archive.
