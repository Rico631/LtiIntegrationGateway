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
| Регистрация outbound-шагов | `ISecretStore`, маршруты продуктов, шаги проверки и вызова; `StoredOutboundResultFactory` восстанавливает результат для повтора и диспетчера |
| Регистрация диспетчера результатов | `Configure<OutboundDeliveryOptions>` и `AddHostedService<OutboundResultDispatcher>`; параметры — ключи `Outbound:DeliveryDispatcher` |
| Регистрация инфраструктуры исходящих вызовов | пул `ChannelHttpClientPool` (`IChannelHttpClientFactory`) и `IChannelCertificateStore`; `AllowAutoRedirect = false`, TLS 1.2+ (ADR §7.5, §7.7) |
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
| `Features/ProductRoutes` | маршруты продуктов: разрешённые каналы и вычисленный адрес очереди `<product_id>.inbox` | spec `outbound-dispatch` |
| `Features/Outbound` | обработка `OutboundRequest`: проверки, авторизация, HTTP с повторами, операция и журнал, публикация результата, восстановление сохранённого результата, фоновый диспетчер доставки | spec `outbound-dispatch` |
| `Features/Contracts` | версии OpenAPI-контрактов: загрузка, публикация со схемами тела и переносом настроек операций, настройки приёма и настройки операции, активация, чтение версии и операций | spec `contract-registry`, `management-api` |
| `Features/Inbound` | приём входящего вызова: проверки, аутентификация, публикация `InboundMessage`, журнал, отказ отключённой операции | spec `inbound-dispatch`, `inbound-routing` |
| `Features/Management` | gRPC-фасад управляющего API и маппинг proto ↔ домен | spec `management-api` |
| `Infrastructure/Persistence` | `GatewayDbContext`, сущности, миграции | ADR §11 |
| `Infrastructure/Rebus` | `ProtobufSerializer`, `ProtobufMessageRegistry`, конфигурация шины | ADR §5.3, §10 |
| `Infrastructure/OpenApi` | чтение документа, fail-closed проверки, извлечение и кэширование схем тела | ADR §14 |
| `Infrastructure/Routing` | каталог маршрутов, источник endpoint'ов, валидатор набора, метаданные маршрута, лимит частоты | ADR §5.2, §9.6 |
| `Infrastructure/Http` | `ChannelHttpClientKey`, `ChannelHttpClientPool` (`IChannelHttpClientFactory`), аренда обработчика: пул соединений по ключу канала | ADR §7.7 |
| `Infrastructure/Security` | `ISecretStore`, `IChannelCertificateStore` и реализации поверх `IConfiguration`; кэш клиентских сертификатов на время жизни процесса | ADR §7.5, §13 |
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
├── GetChannelHealth         GetChannelHealthHandler: активная ревизия, секрет, срок сертификата, последние ошибки
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
├── ResolveSenderIdentity         product_id + rbs2-sender-address по ProductRoutes
├── LoadActiveChannel             активная ревизия канала
├── RelativePathMatcher           структурные запреты и шаблоны пути
├── ResolveAuthorization          none / bearer / api_key / basic / client_certificate
├── ExecuteHttpCall               HTTP с повторами по политике канала, обработчик из пула
├── OutboundOperationRecorder     операция и журнал одной транзакцией
├── StoredOutboundResultFactory   восстановление результата из операции и журнала
├── PublishOutboundResult         Send результата и признак доставки
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
├── ActivateContractVersion        каталог маршрутов + кэш схем → транзакция БД
├── ListContractOperations         операции версии с настройками
├── GetContractVersion             версия с настройками и операциями
├── UpdateContractVersionSettings  настройки приёма черновика
├── UpdateContractOperationSettings настройки операции: enabled, очередь, квота; применение без перезапуска
├── ContractVersionSettings        настройки приёма версии (record)
├── ContractOperationSettings      действующие настройки операции: операция → версия → приложение (record)
└── ContractRouteSets              InboundRouteSet из операций версии с действующими настройками
```

### Входящий контур (spec `inbound-dispatch`)

```text
Features/Inbound
├── InboundRequestHandler          порядок проверок, InboundMessage, журнал, 202/503
├── InboundRequestPath             фактический путь запроса без query и декодирования
├── InboundRequestAuthenticator    none, api_key, basic, client_certificate
├── InboundDisabledOperationHandler 404 без чтения тела и аутентификации + аудит InboundOperationDisabled
├── InboundMessageLogWriter        запись pending → sent | failed
└── InboundMessagePublisher        IInboundMessagePublisher + Rebus-реализация

Infrastructure/Routing
├── InboundRouteDefinition     маршрут с действующими настройками операции и признаком Enabled
├── InboundRouteCatalog        активные наборы, снапшот, токен изменения
├── InboundRouteSetValidator   методы, шаблоны, конфликты (включая отключённые операции)
├── InboundEndpointDataSource  RouteEndpoint'ы, метаданные маршрута, делегат приёма или отказа
├── InboundChannelMetadata     канал как ключ лимита частоты
├── InboundRouteMetadata       ревизия, операция, очередь, авторизация, квота
└── InboundRateLimitOptions    квота и окно по умолчанию

Infrastructure/OpenApi
├── InboundSchemaCache         компиляция схем на ревизию, удаление при деактивации
└── OpenApiDocumentValidator   извлечение схем тела с инлайнингом локальных $ref
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
| `Persistence/Entities` | каналы и ревизии, маршруты продуктов, контракты с настройками приёма, операции со схемами, журналы | опубликованные ревизии и версии контрактов неизменяемы |
| `Persistence/Migrations` | `InitialChannelRegistry`, `AddOutboundDispatch`, `AddContractRegistry`, `AddInboundDispatch`, `AddOutboundResultDispatcher`, `AddContractOperationSettings` | применяются при старте |
| `Rebus/ProtobufSerializer` | `ISerializer` на ProtoJSON | заголовки `rbs2-msg-type` и `rbs2-content-type` |
| `Rebus/ProtobufMessageRegistry` | реестр типов сообщений | неизвестный тип — диагностируемая ошибка |
| `Rebus/RebusConfigurationExtensions` | регистрация шины, error-queue и обработчика | входная очередь `lti.gateway.outbound`, error-queue `lti.gateway.error` |
| `OpenApi/MicrosoftOpenApiDocumentReader` | чтение документа, версия, синтаксис | `Microsoft.OpenApi` 3.11.0 (ADR §14) |
| `OpenApi/OpenApiDocumentValidator` | fail-closed проверки, извлечение операций и схем тела | запрещённые ключевые слова, `$ref`, `format`; инлайнинг `$ref` до глубины 32 |
| `OpenApi/InboundSchemaCache` | скомпилированные схемы активных ревизий | `NJsonSchema` 11.6.1, компиляция при активации |
| `Management/ManagementApiKeyMiddleware` | ключ управляющего API | `401`/`503`, gRPC `Unauthenticated` |
| `Management/ManagementAuditWriter` | аудит управляющих операций, включая чтение и отказы `NotFound` | без значений секретов; усечение объекта и деталей до длин модели |
| `Security/ConfigurationSecretStore` | значение секрета по ключу | только `IConfiguration` |
| `Security/ConfigurationChannelCertificateStore` | клиентский сертификат по `config_key` | PFX из файла или base64, пароль `<config_key>:Password`, кэш на время жизни процесса |
| `Http/ChannelHttpClientPool` | пул `SocketsHttpHandler` по ключу канала | TLS 1.2+, без редиректов, проверка серверного сертификата, срок жизни соединения 5 минут |

Имена очередей сосредоточены в `Rebus/QueueNames` (ADR §10.2, решение 28): входная очередь
`lti.gateway.outbound`, error-queue `lti.gateway.error` (настраивается `RetryStrategy` с сохранением
значений по умолчанию для числа попыток и кулдауна), очередь продукта вычисляется как
`<product_id>.inbox`. Список `ProductRouteQueueAddress` остаётся в модели для обратной
совместимости чтения, но проверки `rbs2-sender-address` и `rbs2-return-address` идут против
вычисленного адреса; регистрация маршрута принимает пустой список или вычисленный адрес.

## 6. Контракты и генерация кода

- `Protos/v1/integration_messages.proto` — сообщения Rebus (`lti.gateway.messages.v1`):
  `OutboundRequest`, `OutboundResult`, `InboundMessage`, `QueryParameter`, `Outcome`.
- `Protos/v1/management_api.proto` — управляющий API (`lti.gateway.management.v1`): один сервис
  `ManagementService`, HTTP-аннотации для REST через transcoding. Состояние канала отдаёт RPC
  `GetChannelHealth` (`GET /v1/channels/{channel_name}/health`); ответ содержит состояние активной
  ревизии, признак настроенного секрета, срок действия клиентского сертификата и последние ошибки;
  используются `google.protobuf.Timestamp`.
- `Protos/google/api/*.proto` — вендорные дескрипторы; подключаются только как путь импорта,
  C#-типы берутся из `Google.Api.CommonProtos` (иначе конфликт типов CS0436).
- Генерация выполняется при сборке (`Grpc.Tools`); сгенерированные файлы не редактируются и в
  репозитории не хранятся.
- Обязательное требование к документации контрактов — `AGENTS.md`.

## 7. Потоки и транзакционные границы

**Outbound.** `OutboundRequest` → проверки (поля, продукт и вычисленный адрес отправителя
`<product_id>.inbox`, тот же адрес для доставки результата, идемпотентность, активная ревизия
канала, метод и путь, авторизация) → операция `Accepted` → `InProgress` → HTTP-вызов с повторами →
**одна транзакция**: операция `Completed` + запись журнала
`pending` (с адресом доставки, `correlation_id`, текстом ошибки, номером ревизии канала и
`CreatedAtUnixMs`) → `Send` результата → журнал `sent`. Отказы фиксируются в журнале и возвращаются
продукту (ADR §8, spec `outbound-dispatch`).

Недоставленные записи `pending` повторно отправляет `OutboundResultDispatcher`: выборка идёт в SQL по
индексу `(DeliveryStatus, CreatedAtUnixMs)` с фильтром «адрес не пуст и операция существует», поэтому
недоставляемые записи не занимают пачку; отправка и фиксация статуса обрабатываются по отдельности, а
гарантия остаётся «минимум один раз». Повторный `OutboundRequest` с завершённым `message_id` отдаёт
идентичный сохранённый результат на адрес из текущего сообщения; владение `product_id` проверяется до
выдачи результата, а проверки канала, метода, пути и авторизации для завершённой операции не
повторяются (ADR §8.9, §8.10).

**Контракты.** Загрузка создаёт черновик версии; публикация извлекает операции и схемы тела
(локальные `$ref` разворачиваются), требует очередь получателя в настройках версии формата
`<product_id>.inbox` (ADR §15.2, §10.2),
дополнительно проверяет действующую очередь каждой включённой операции, переносит настройки операций
из предыдущей версии канала (по `operationId`, а при его отсутствии хотя бы у одной из операций — по
совпадению метода и шаблона; изменённый `operationId` при том же маршруте — новая операция) и
проверяет конфликты; активация сначала регистрирует набор в каталоге маршрутов, затем одной
транзакцией меняет статусы версий,
снимает набор предыдущей версии и пересобирает кэш схем; при проблемах каталога база не меняется
(ADR §5.2, spec `contract-registry`).

**Настройки операции.** Действующие значения вычисляются при построении набора маршрутов
(`ContractRouteSets`): очередь, квота и окно — операция, иначе версия, квота и окно версии — иначе
`Inbound:RateLimit:*`; авторизация берётся только из версии. Операции извлекаются при публикации,
поэтому для черновика настройки операции недоступны (`404`). Изменение настроек версии `Active`
перерегистрирует набор с тем же ключом ревизии: отказ каталога оставляет прежний набор и не меняет
базу; версия `Superseded` отклоняется с проблемой `version` (ADR §7.3, §15.2).

**Входящий контур.** Запрос попадает на динамический endpoint с метаданными маршрута, далее
фиксированный порядок: query (≤ 64) → `Content-Type` по схемам ревизии → наличие схемы тела →
аутентификация по настройкам ревизии → глубина JSON (≤ 64) → валидация по схеме → запись журнала
`pending` → `Send` в очередь получателя → журнал `sent` и `202`. Любой отказ пишется в журнал
`failed` с кодом ошибки; при сбое `Send` ответ `503` (ADR §9.6, spec `inbound-dispatch`).
Отключённая операция обслуживается отдельным делегатом: тело не читается, аутентификация не
выполняется, в `ManagementAuditEntry` пишется `InboundOperationDisabled`, ответ — `404`.

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
| Сортировка по `DateTimeOffset` не используется в SQLite-запросах | SQLite не поддерживает такие `ORDER BY`; порядок записей журнала задаётся колонками `CreatedAtUnixMs` (исходящие) и `ReceivedAtUnixMs` (входящие), в памяти сортируется только ограниченное окно |
| Строковые поля proto заполняются только непустыми значениями | присваивание `null` бросает `ArgumentNullException` |
| Динамические endpoint'ы добавляются в `IEndpointRouteBuilder.DataSources` | регистрации `EndpointDataSource` в DI недостаточно (ADR §5.2) |
| gRPC-сервисы публикуются в корне | transcoding сохраняет пути `/v1/...`; следствие — двухсегментные inbound-маршруты отвечают `404` вместо `405` (ADR §15.2) |
| Секреты читаются только через `ISecretStore` | в базе и API хранятся лишь ключи конфигурации |
| Схемы тела компилируются один раз на ревизию | валидация не тратит CPU на каждый запрос (ADR §14) |
| Публикация сообщения идёт через `IInboundMessagePublisher` | транспорт изолирован, сбой передачи наблюдаем в тестах |
| Запись журнала `pending` создаётся до `Send` | сбой передачи виден в журнале, а не теряется |
| Сравнение секретов — `CryptographicOperations.FixedTimeEquals` | исключение утечки по времени (ADR §13) |

## 9. Тесты

Структура повторяет производственные каталоги; общая обвязка — `TestSupport`.

| Каталог | Что проверяется |
|---|---|
| `Contracts` | round-trip ProtoJSON, профиль кодирования, дескрипторы, `MessageContractValidator` |
| `Features/Host` | liveness, конфигурация хранилища, отсутствие демо-API, регистрация обработчиков |
| `Features/Channels` | валидатор настроек, жизненный цикл ревизий, сохранение до `DbContext` |
| `Features/Outbound` | успешный вызов, отказы, авторизация, повторы, идемпотентность, идентичность повтора, диспетчер недоставленных результатов |
| `Features/Inbound` | маршрутизация, приём вызова, аутентификация, коды ответов, журнал, лимит частоты, отключённая операция и аудит |
| `Features/Contracts` | загрузка и проверка документов, публикация, настройки версии, активация маршрутов |
| `Features/Management` | ключ API, паритет gRPC/REST, каналы, маршруты, статус операции |
| `Infrastructure/*` | сериализация и реестр Rebus, `DbContext`, каталог маршрутов, чтение OpenAPI, извлечение и компиляция схем, пул HTTP-обработчиков, хранилище сертификатов |

`TestSupport`:

- `SqliteInMemoryDatabase` — SQLite `:memory:` с открытым соединением на время теста;
- `GatewayWebApplicationFactory` — хост с подменой строки подключения и дополнительными ключами
  конфигурации;
- `OutboundFixture` — SQLite, тестовый HTTP-обработчик и два in-memory endpoint'а Rebus; собирает
  диспетчер так же, как приложение (контекст БД на цикл);
- `InboundFixture` — SQLite, тестовые секреты, `DefaultHttpContext` с метаданными маршрута и два
  in-memory endpoint'а Rebus (шлюз и продукт), подмена публикатора для сбоя передачи;
- `ManagementApi` — REST-клиент с ключом и in-process gRPC-канал;
- `RecordingBus` — шина, фиксирующая адрес и сообщение `Send` и имитирующая сбой отправки;
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
| `2026-10-29-inbound-dispatch` | `inbound-dispatch` (+ `inbound-routing`, `contract-registry`, `management-api`) | приём вызова, аутентификация, схемы тела, журнал `InboundMessageLog`, настройки версии контракта, миграция `AddInboundDispatch` |
| `2026-10-31-outbound-client-certificate` | `outbound-dispatch` | `Infrastructure/Http` (пул `SocketsHttpHandler` по ключу канала), `IChannelCertificateStore`, авторизация `client_certificate` |
| `2026-10-31-outbound-result-dispatcher` | `outbound-dispatch` | `OutboundResultDispatcher`, `StoredOutboundResultFactory`, адрес доставки и ревизия в журнале, индекс `pending`, миграция `AddOutboundResultDispatcher` |
| `2026-10-31-inbound-operation-settings` | `contract-registry`, `inbound-routing`, `management-api` | настройки операции (`enabled`, очередь, квота), `PATCH .../operations/{operationId}`, отключённые маршруты `404` + аудит, миграция `AddContractOperationSettings` |
| `2026-10-31-channel-health` | `management-api` | срез `GetChannelHealth`, RPC `GetChannelHealth` (`GET /v1/channels/{channel_name}/health`), `ChannelCertificateInfo` для диагностики сертификата, журнал `InboundMessageLog.ReceivedAtUnixMs`, индексы «канал + время» `(ChannelName, AtUnixMs)` в обоих журналах, миграции `AddInboundReceivedAtUnixMs`, `AddOutboundErrorIndex` |
| `2026-10-31-queue-naming` | `message-serialization`, `outbound-dispatch`, `contract-registry` | `QueueNames` (входная очередь `lti.gateway.outbound`, error-queue `lti.gateway.error`), вычисляемый адрес продукта `<product_id>.inbox`: регистрация и чтение маршрута (проблема `address`), проверка `rbs2-sender-address` и `rbs2-return-address`, формат очереди версии контракта при публикации (проблема `queue`) |
| `2026-10-31-complete-management-api` | `management-api` | аудит чтения канала и отказов `NotFound`, выравнивание комментариев контракта и README |

## 11. Поддержание актуальности

После архивации изменения:

1. обновить разделы 1–9 под фактическое состояние кода (новые каталоги, срезы, сущности, миграции,
   соглашения);
2. добавить строку в таблицу раздела 10;
3. удалить описания того, что было удалено или переименовано;
4. проверить, что упомянутые пути существуют и ссылки на ADR/спеки указывают на актуальные разделы.

Требования и правила в этот файл не переносятся — здесь фиксируется только фактическая организация
кода.
