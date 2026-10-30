# Design

## Context

См. `proposal.md` — Why. Существенное для реализации:

- ADR §5.1: управляющий API — gRPC-сервисы в `.proto` с HTTP-аннотациями, REST-доступ через JSON
  transcoding; §13: доступ по одному ключу из переменных окружения, сравнение за постоянное время,
  `401` без деталей; §14: OpenAPI 3.0 принимается, Swagger 2.0 конвертируется, 3.1 отклоняется,
  проверки fail-closed с перечнем запрещённых конструкций и правилами `$ref`; §15: перечень операций
  и неизменяемость опубликованных ревизий.
- Текущий код: срезы `Features/Channels` (`CreateChannel`, `UpdateChannelDraft`,
  `PublishChannelRevision`, `ActivateChannelRevision`, `RollbackChannelRevision`, `GetChannel`,
  `ChannelConfigurationValidator`), `ChannelConfiguration` как запись настроек, `ChannelAuditWriter`
  (внутренний, пишет аудит в той же транзакции), срезы `Features/ProductRoutes`,
  `Infrastructure/Routing` (`InboundRouteCatalog.Activate/Deactivate`, `InboundRouteSetValidator`).
- Пакетов `Grpc.AspNetCore`, `Microsoft.AspNetCore.Grpc.JsonTranscoding`, `Microsoft.OpenApi` в
  проекте нет; gRPC-хостинг и REST-трансляция добавляются в этом изменении.
- Тесты: TUnit, `GatewayWebApplicationFactory` с подменой строки подключения и дополнительными
  ключами конфигурации.

## Goals / Non-Goals

Цели дизайна:

- один Protobuf-контракт управляющего API для gRPC и REST, без дублирования REST-моделей;
- переиспользование существующих срезов: API — тонкий адаптер, доменные правила остаются в срезах;
- проверки OpenAPI отделены от валидации тел (её в этом изменении нет).

Вне дизайна:

- валидация входящего тела (`NJsonSchema`) и публикация `InboundMessage`;
- настройки отдельной операции, состояние канала (`health`), персональная идентичность админа;
- эволюция контракта для продуктов (`lti.gateway.messages.v1` не меняется).

## Decisions

### D1. Отдельный Protobuf-контракт управляющего API

`Protos/v1/management_api.proto`, `package lti.gateway.management.v1`,
`option csharp_namespace = "LtiIntegrationGateway.Contracts.Management.V1"`, `service ManagementService`.
REST достижим только через HTTP-аннотации `google.api.http`; повторно описанных REST-DTO нет.

RPC (по §15.2, окончательный перечень):

```text
ListChannels                 GET    /v1/channels
CreateChannel                POST   /v1/channels
GetChannel                   GET    /v1/channels/{name}
UpdateChannelDraft           PATCH  /v1/channels/{name}/draft
PublishChannelRevision       POST   /v1/channels/{name}/revisions/{revision}/publish
ActivateChannelRevision      POST   /v1/channels/{name}/revisions/{revision}/activate
RollbackChannelRevision      POST   /v1/channels/{name}/rollback
RegisterProductRoute         PUT    /v1/products/{product_id}/route
GetProductRoute              GET    /v1/products/{product_id}/route
UploadContract               POST   /v1/channels/{name}/contracts
PublishContractVersion       POST   /v1/channels/{name}/contracts/{version}/publish
ActivateContractVersion      POST   /v1/channels/{name}/contracts/{version}/activate
ListContractOperations       GET    /v1/channels/{name}/contracts/{version}/operations
GetOutboundOperation         GET    /v1/operations/{message_id}
```

**Почему отдельный package:** потребитель — административный контур, эволюция не должна совпадать с
эволюцией сообщений для продуктов; смешение в одном `.proto` связало бы версии.

**Почему один сервис:** операции управляют одной моделью конфигурации; разнесение по сервисам
добавило бы контрактов без пользы для одной административной роли.

**Альтернатива:** REST на minimal API без gRPC. Отклонено: ADR §5.1 фиксирует gRPC+transcoding как
единственный способ дать оба доступа из одного контракта.

### D2. Аутентификация — middleware на пути управляющего API

`ManagementApiKeyMiddleware` проверяет заголовок (по умолчанию `X-Management-Api-Key`, имя
настраивается) для путей управляющего API: `/v1/**` и `/lti.gateway.management.v1.ManagementService/**`.

```text
ключ не настроен (Management:ApiKey пуст)  -> 503, операция не выполняется
заголовка нет или значение не совпало       -> 401 без тела и деталей
совпало (FixedTimeEquals, UTF-8 байты)      -> дальше по pipeline
```

**Почему middleware, а не gRPC interceptor:** REST-запросы через transcoding идут обычным HTTP
pipeline; interceptor покрыл бы только gRPC-вызовы, и требования к обоим доступам разошлись бы.

**Почему `503` при отсутствии ключа:** конфигурация, при которой управляющий API открыт всем,
недопустима; отказ лучше «тихого» пропуска без проверки (тот же принцип, что §13 для каналов).

### D3. Аудит управляющих операций

Новая сущность `ManagementAuditEntry`: `Operation`, `Target`, `Result` (`succeeded`/`rejected`),
`Details`, `OccurredAt`. Записи пишутся для успешных и отклонённых операций; `Details` содержит коды
проблем без значений секретов. Аудит изменений каналов остаётся в `ChannelAuditEntry` (пишется
срезами в их транзакции) — дублирования нет, потому что это разные уровни: доменное изменение и
факт административного вызова.

**Почему отдельная таблица:** управляющий уровень фиксирует вызовы, которые не меняют домен
(чтение, отказ по ключу, отказ валидации), а `ChannelAuditEntry` привязан к каналу и ревизии.

### D4. Отображение proto ↔ срезы и ошибки

Прототипные сообщения — плоские DTO; маппинг в `ChannelConfiguration` и обратно выполняется в
`Features/Management/ChannelMapping`. Доменные проблемы возвращаются полем
`repeated OperationProblem problems { code, message }` в ответе; отсутствие объекта — gRPC-статус
`NotFound`; несовпадение предусловия (например, изменение опубликованной ревизии) — `FailedPrecondition`.
Транспортные статусы не смешиваются с доменными проблемами: `400`/`InvalidArgument` для
некорректного запроса, `problems` — для отклонённой доменной операции.

### D5. Модель данных контрактов

```text
ContractVersion
    Id, ChannelId (FK), Version (int, уникально в канале), SourceFormat (OpenApi30 | Swagger20),
    Document (исходный текст), Status (Draft | Published | Active | Superseded),
    CreatedAt, PublishedAt, ActivatedAt
ContractOperation
    Id, ContractVersionId (FK), HttpMethod, Template, OperationId (nullable)
```

Контракт принадлежит каналу, потому что и маршруты, и лимиты частоты, и очередь получателя в ADR
привязаны к каналу; отдельная сущность «контракт» без канала потребовала бы второй связи.

Активной может быть только одна версия канала: при активации предыдущая `Active` переходит в
`Superseded` в той же транзакции.

**Почему документ хранится исходным текстом:** опубликованная ревизия должна быть воспроизводима
(§14), а конвертация 2.0 → 3.0 фиксируется в аудите, но не заменяет источник.

### D6. Разбор и проверки OpenAPI

```text
Infrastructure/OpenApi
    IOpenApiDocumentReader            Task<OpenApiReadResult> ReadAsync(string document, CancellationToken)
    MicrosoftOpenApiDocumentReader    чтение и определение версии (Microsoft.OpenApi)
    OpenApiDocumentValidator          запрещённые конструкции, $ref, format, операции
    OpenApiReadResult                 SourceFormat, Version, Operations, Problems, Warnings
```

Проверки (fail-closed, все проблемы за один проход):

1. версия: `openapi: 3.0.x` — принимается; `swagger: "2.0"` — принимается с конвертацией;
   `3.1.x` и всё остальное — отказ;
2. обход документа как `JsonNode` для поиска запрещённых ключевых слов (`if`, `then`, `else`,
   `const`, `patternProperties`, `propertyNames`, `contains`, `dependencies`,
   `unevaluatedProperties`, `unevaluatedItems`, `prefixItems`, `$dynamicRef`, `$anchor`,
   `$recursiveRef`) с путём до элемента;
3. `$ref`: разрешены только `#/...`; всё, что содержит схему, `//`, `file`, относительный путь или
   выход за пределы документа, — отказ;
4. поля рядом с `$ref` — отказ с подсказкой про `allOf` или отдельную схему;
5. `format`: фиксированный набор; неизвестный — предупреждение, не отказ.

**Почему два прохода (модель и `JsonNode`):** модель `Microsoft.OpenApi` нормализует документ и
теряет неизвестные ключевые слова и соседние с `$ref` поля, поэтому запрещённые конструкции нужно
искать в исходном JSON, а операции и схемы удобнее брать из модели.

**Риск и первый шаг реализации:** API `Microsoft.OpenApi` 3.x отличается от 1.x, и поддержка
Swagger 2.0 в нём не подтверждена. Первая задача — пробный тест, фиксирующий фактические имена API,
поведение загрузки и наличие конвертации 2.0 → 3.0. Если конвертации нет — берём
`Microsoft.OpenApi.Readers` 1.6.x или собственную конвертацию, и фиксируем выбор в ADR §14.
Версия пакета определяется по результату, а не назначается заранее.

**Проверено (задача 1.1):** `Microsoft.OpenApi` **3.11.0** читает и OpenAPI 3.0, и Swagger 2.0 через
`OpenApiDocument.LoadAsync(Stream)`, возвращая `ReadResult { Document, Diagnostic }`; версия
источника доступна в `Diagnostic.SpecificationVersion` (`OpenApi3_0` / `OpenApi2_0`), ошибки и
предупреждения — в `Diagnostic.Errors` / `Diagnostic.Warnings`. Факт зафиксирован пробным тестом
`OpenApiLibraryProbeTests`; отдельный пакет-ридер не нужен.

### D7. Публикация и активация версии контракта

Публикация: операции извлекаются из разобранного документа, проверяются уникальность `operationId` и
конфликт «метод + шаблон» с активными маршрутами других ревизий (переиспользуется
`InboundRouteSetValidator`), статус версии меняется на `Published`.

Активация выполняется в таком порядке:

1. построить `InboundRouteSet(revisionKey: $"contract:{versionId}", routes)` и вызвать `Activate`
   каталога;
2. при проблемах — отказ без изменения базы, активная версия и её маршруты сохраняются;
3. при успехе — в одной транзакции: новая версия `Active`, предыдущая `Superseded`; затем
   `Deactivate` набора предыдущей версии.

**Почему сначала активация маршрутов, потом база:** если упасть между шагами, лишние маршруты
временны и снимаются повтором активации; обратный порядок оставил бы активную версию без маршрутов.

### D8. Чтение статуса исходящей операции

`GetOutboundOperation` читает `OutboundOperations` и, если есть, последнюю запись
`OutboundMessageLogs` для HTTP-статуса. Только чтение, без побочных эффектов.

### D9. Тесты

- gRPC: `WebApplicationFactory` + `Grpc.Net.Client` с `TestServer`-handler, чтобы вызывать сервис
  in-process; REST — обычный `HttpClient` фабрики (transcoding).
- Ключ API задаётся конфигурацией фабрики (`GatewayWebApplicationFactory.WithStorage(settings)`).
- OpenAPI-проверки — юнит-тесты на документах-образцах (3.0, 2.0, 3.1, запрещённые конструкции,
  внешние `$ref`, соседние поля, неизвестный `format`).
- Активация контракта проверяется в паре: маршрут обслуживается после активации и отвечает `404`
  после активации версии без этой операции (используется уже проверенный каталог маршрутов).
- Аудит проверяется по таблице `ManagementAuditEntries` и по отсутствию значений секретов.

## Risks / Trade-offs

- **[Неизвестный API `Microsoft.OpenApi` 3.x]** → первый шаг — пробный тест; при несовпадении
  фиксируется другая версия/пакет и обновляется ADR §14.
- **[Transcoding и порядок middleware]** → тесты REST-доступа без ключа и с ключом проверяют, что
  аутентификация применяется к обоим доступам.
- **[Две системы состояния при активации]** (база и каталог маршрутов) → порядок шагов в D7 и
  повторная активация идемпотентны; окно рассогласования ограничено одной транзакцией.
- **[Большая миграция]** → две новые таблицы контрактов и одна таблица аудита; существующие таблицы
  не меняются.
- **[Секреты в аудите]** → в `Details` пишутся коды проблем и имена объектов, не значения.
- **[Один ключ без персональной идентичности]** → аудит фиксирует факт операции; переход на JWT/IdP
  остаётся открытым вопросом ADR §20.

## Migration Plan

1. Миграция EF Core добавляет `ContractVersions`, `ContractOperations`, `ManagementAuditEntries`;
   применяется при старте.
2. Порядок выката: конфигурация ключа (`Management:ApiKey`) → приложение; без ключа управляющий API
   отвечает `503`, остальная функциональность не затрагивается.
3. Проверка: `dotnet build`, `dotnet test`, ручной запуск, `/health/live` = `200`.
4. Откат: revert изменения; таблицы удаляются вместе с базой разработки, в эксплуатации данных нет.

## Open Questions

- Персональная идентичность администратора и роли (ADR §20) — вне этого изменения.
- Настройки отдельной операции (`enabled`, очередь получателя, лимит частоты) — следующий change.
- Состояние канала (`/v1/channels/{name}/health`) — отдельное изменение.


