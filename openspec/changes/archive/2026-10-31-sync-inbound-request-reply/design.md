# Design

## Context

См. `proposal.md` — Why. Текущая модель использует общий `Channel`: `ChannelRevision` описывает
outbound-настройки, `ContractVersion` — inbound-контракт, а `ProductRoutes` ссылаются на имя
канала. `InboundRequestHandler` отправляет `InboundMessage` через `IInboundMessagePublisher` и
отвечает `202`; ответного сообщения от продукта нет. ADR §7, §9.5, §10 и §15 описывают текущие
границы; этот change меняет их и требует обновления ADR и постоянных specs.

## Goals / Non-Goals

**Goals:**

- Физически разделить outbound- и inbound-каналы и исключить `BOTH`.
- Сохранить текущий жизненный цикл черновик → публикация → активация для обоих потоков.
- Добавить channel-level `ASYNC|SYNC`, timeout и request/reply для inbound.
- Сохранить совместимость с текущей генерацией inbound endpoints из `swagger.json`.
- Сохранить аудит запросов и ответов без отдельной таблицы ответов.
- Подготовить чистую схему для дальнейшей разработки без внешних потребителей v1.

**Non-Goals:**

- Изменение outbound HTTP-алгоритма, retry и `OutboundResult`.
- Поддержка OpenAPI callbacks/webhooks.
- Дедупликация inbound и распределённая блокировка ожидающих запросов.
- Новая версия management API и обратная совместимость v1.
- Метрики/дашборды сверх существующего observability-среза.

## Decisions

### 1. Два независимых агрегата

`OutboundChannel` и `InboundChannel` — отдельные сущности и таблицы с собственными Id, именем,
audit и жизненным циклом. `ChannelRevision` принадлежит только `OutboundChannel`;
`ContractVersion` — только `InboundChannel`. `Channel` удаляется.

Одинаковые имена разрешены, потому что API и БД разделены по направлению. Management API использует
direction-first ресурсы:

```text
/v1/outbound-channels/...
/v1/inbound-channels/...
```

`BOTH` отсутствует: направление задаётся типом агрегата, а не флагом. Для существующих dev-данных
допускается destructive migration.

Альтернатива — общий `Channel` с двумя профилями — отклонена: она сохраняет неоднозначное состояние
и требует проверок наличия профиля.

### 2. Жизненный цикл

`CreateOutboundChannel` создаёт `OutboundChannel` + черновик `ChannelRevision` 1.
`CreateInboundChannel` принимает OpenAPI-документ и создаёт `InboundChannel` + черновик
`ContractVersion` 1. Публикация и активация — отдельные операции; inbound endpoints появляются
только после активации контракта. Последующие версии контракта загружаются отдельно.

Альтернатива — создавать inbound-канал сразу активным — отклонена: смешивает валидацию,
публикацию и регистрацию маршрутов, лишает отката.

### 3. Channel-level режим и timeout

`InboundChannel.response_mode` — `ASYNC` или `SYNC`; режим действует на все endpoints канала.
`InboundChannel.inbound_response_timeout_ms` — положительное значение без application upper bound;
default `30000`. Поля входят в `CreateInboundChannel` как optional и меняются отдельной PATCH-
операцией с аудитом. Текущий запрос использует snapshot режима и timeout на момент старта.

`ASYNC` сохраняет `202` после успешной отправки. `SYNC` отправляет `InboundMessage` с
`reply_expected = true`, ждёт `InboundResponse` и формирует HTTP-ответ.

### 4. Rebus.Async и контракт ответа

Используется пакет `Rebus.Async` 10.0.0: `EnableSynchronousRequestReply()` на gateway и на всех
product endpoints, `SendRequest<InboundResponse>` из `IRoutingApi` с explicit recipient queue,
timeout и `HttpContext.RequestAborted`. Продукт отвечает `bus.Reply(new InboundResponse(...))`.

`InboundResponse` добавляется в `integration_messages.proto`:

```text
message_id
http_status_code
content_type
body: bytes
repeated InboundResponseHeader headers
error_code
```

`InboundResponseHeader` содержит `name` и `value`. `message_id` обязателен и должен совпадать с
исходным. Допустимы только `200..299` и `400..599`; `1xx`/`3xx` отклоняются. Body может отсутствовать;
при непустом body `content_type` обязателен. `error_code` — метка журнала/аудита, а HTTP-статус
определяет только `http_status_code`.

Альтернатива — собственный correlation registry без `Rebus.Async` — отклонена: больше кода,
таймеров и обработки поздних ответов, при том что продукты наши и могут включить пакет.

### 5. Заголовки ответа

Разрешены только `Location`, `Retry-After`, `Cache-Control`, `ETag`, `Last-Modified`, `Vary`,
`WWW-Authenticate`. `Content-Type` берётся из `content_type`, а не из headers. Запрещены
`Set-Cookie`, `Content-Length`, `Transfer-Encoding`, `Connection`, `Host`, `Server`, `Date`,
`Authorization` и неизвестные заголовки. Нарушение — fail-closed: весь ответ отклоняется,
`502 invalid_inbound_response`, событие пишется в лог и аудит без значения заголовка.

### 6. Ошибки и журнал

Timeout → `504`, `TimedOut`, `inbound_timeout`; отмена клиента → `Cancelled`; send failure → `503`;
некорректный ответ → `502`, `Failed`, `invalid_inbound_response`; поздний ответ игнорируется.
Повторной отправки inbound gateway не делает.

`InboundMessageLog` остаётся одной записью: добавляются `ResponseStatusCode`,
`ResponseContentType`, `ResponseBody`, `ResponseHeaders` (JSON), `ErrorCode`; статусы `Replied`,
`TimedOut`, `Cancelled`, `Failed`, `Sent`. Заголовки ответа хранятся JSON-массивом, без отдельной
таблицы. Async-путь остаётся `Sent`.

### 7. Management API

Один `ManagementService`; package `lti.gateway.management.v1` переписывается как breaking change.
RPC именуются direction-first: `ListOutboundChannels`, `CreateOutboundChannel`,
`UpdateInboundChannelSettings`, `UploadInboundContractVersion` и т.д. Product routes ссылаются только
на outbound-каналы; контракты адресуются только inbound-каналами.

### 8. Миграция

Все существующие миграции удаляются, создаётся новая базовая схема с `OutboundChannels`,
`InboundChannels`, новыми FK и полями `InboundMessageLog`. Dev-данные не сохраняются. Причина —
физическое разделение несовместимо со старой таблицей `Channels`; сохранять смешанные записи
означало бы вводить временную неоднозначную модель.

## Component changes

- `Protos/v1/integration_messages.proto`: `reply_expected` в `InboundMessage`; новые `InboundResponse`
  и `InboundResponseHeader`.
- `Protos/v1/management_api.proto`: direction-first сообщения/RPC, `InboundChannel` settings,
  `CreateInboundChannel` с документом; старые channel-сообщения и пути удаляются/переписываются.
- `Infrastructure/Persistence`: `OutboundChannel`, `InboundChannel`, FK `ChannelRevision` и
  `ContractVersion`, поля ответа `InboundMessageLog`, новая базовая миграция.
- `Features/Channels` и `Features/Contracts`: отдельные handlers для outbound/inbound; создание
  inbound-канала атомарно создаёт черновик контракта.
- `Features/Inbound`: `reply_expected`, ожидание `InboundResponse`, timeout/cancellation, response
  mapping и обновление журнала.
- `Features/Management`: один service с direction-first RPC; product routes разрешают только
  outbound-каналы.
- `Infrastructure/Rebus`: новая регистрация request/reply, `Rebus.Async`; `InboundMessagePublisher`
  получает метод sync-запроса.
- `Program.cs`: регистрация `Rebus.Async`, обновление startup secret validation и health для двух
  типов каналов.
- Наблюдаемость: существующий inbound span/метрики дополняются исходом sync-ожидания
  (`reply`/`timeout`/`cancelled`/`invalid`), без новых высококардинальных тегов.

Транзакции: создание канала и первого черновика контракта/ревизии выполняется одной транзакцией БД.
В sync-пути запись журнала `Pending` создаётся до отправки; после ответа/ошибки обновляется та же
запись. Отправка в Rebus не входит в транзакцию БД, поэтому возможны дубли и потеря ожидания при
падении процесса; это фиксируется в ADR и документации.

## Risks / Trade-offs

- **Долгое удержание HTTP-соединения** → timeout настраивается, но верхняя граница отсутствует;
  фактический предел задают клиент, прокси и инфраструктура.
- **Падение gateway во время ожидания** → pending state теряется, поздний ответ продукта
  игнорируется; клиент получает разрыв/ошибку. Это ограничение `Rebus.Async`, а не БД.
- **Отсутствие дедупликации** → timeout и повтор клиента могут привести к повторной обработке
  события продуктом; поведение остаётся прежним и документируется.
- **Хранение response body/headers** → увеличивает объём `InboundMessageLog`; принимается как
  требование аудита.
- **Fail-closed по заголовкам** → продукт с неверным заголовком получает `502`; это обнаружение
  ошибки контракта, а не скрытое исправление.
- **Breaking management API** → все клиенты обновляются вместе с gateway; v1 compatibility не
  создаётся.
- **Зависимость от `Rebus.Async` на обеих сторонах** → продукт без `EnableSynchronousRequestReply`
  будет получать timeout; deployment должен обновлять gateway и продукты согласованно.

## Migration Plan

1. Остановить dev-окружение и удалить старые миграции/БД.
2. Создать новую базовую миграцию с `OutboundChannels`, `InboundChannels`, новыми FK и журналом.
3. Обновить gateway и все product endpoints одновременно: `Rebus.Async`,
   `EnableSynchronousRequestReply`, обработку `reply_expected` и `InboundResponse`.
4. Создать outbound- и inbound-каналы заново через новый management API.
5. Проверить async-регрессию, sync success/timeout/cancel/invalid-response, аудит и health.

Откат: вернуть предыдущую версию gateway/продуктов и предыдущую БД; данные dev-окружения
восстанавливаются из резервной копии, автоматическая обратная миграция не предусмотрена.

## Open Questions

Нет. Все решения, влияющие на specs, API и tasks, зафиксированы выше; оставшиеся операционные
настройки (конкретные значения timeout для среды) задаются при развёртывании.
