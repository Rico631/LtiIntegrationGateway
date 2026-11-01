# Proposal

## Why

Сейчас `Channel` — общий агрегат для двух независимых потоков: outbound использует его для HTTP-вызовов, inbound — для OpenAPI-контрактов. Из-за этого management API позволяет загрузить `swagger.json` в outbound-канал, а inbound не имеет собственного режима ответа. Нужно физически разделить потоки и добавить синхронный inbound request/reply: внешний REST-вызов ждёт ответ продукта через Rebus и получает HTTP-ответ, сформированный из этого ответа.

## What Changes

- **BREAKING**: удалить общий `Channel` и разделить его на независимые `OutboundChannel` и `InboundChannel`. Одинаковые имена в разных направлениях разрешены; `BOTH` не существует.
- `OutboundChannel` создаётся с черновиком ревизии 1; `InboundChannel` создаётся с обязательным `swagger.json`/`openapi.json` и черновиком контракта 1. Публикация и активация остаются отдельными шагами.
- **BREAKING**: переписать `management_api.proto` в существующем package `lti.gateway.management.v1` без обратной совместимости: direction-first ресурсы `/v1/outbound-channels` и `/v1/inbound-channels`.
- Добавить `InboundChannel.response_mode = ASYNC | SYNC` (по умолчанию `ASYNC`) и `inbound_response_timeout_ms` (по умолчанию `30000`, без верхнего ограничения). Оба поля можно менять через `PATCH` с аудитом.
- В `ASYNC` сохранить текущее поведение: после успешной отправки `InboundMessage` внешний клиент получает `202`.
- В `SYNC` отправлять `InboundMessage` с `reply_expected = true`, удерживать HTTP-соединение до `InboundResponse` или timeout и возвращать статус/body/заголовки продукта.
- Добавить `InboundResponse` с `message_id`, `http_status_code`, `content_type`, `body`, повторяемыми `headers` и `error_code`.
- Ограничить статусы ответа продукта `200..299` и `400..599`; `1xx`/`3xx` считать некорректным ответом. Body может отсутствовать; при непустом body обязателен `content_type`.
- Разрешить только безопасный allowlist заголовков ответа; неизвестные/запрещённые заголовки отклонять fail-closed с `502`.
- Расширить `InboundMessageLog` статусами `Replied`, `TimedOut`, `Cancelled`, `Failed` и полями статуса, content-type, body и JSON-заголовков ответа в той же записи.
- Удалить старые миграции и создать новую схему с нуля; существующие dev-данные не сохраняются.

### Вне объёма

- Изменение outbound-алгоритма HTTP-вызовов, retry и доставки `OutboundResult`.
- OpenAPI `callbacks`/`webhooks`: inbound endpoints по-прежнему строятся только из `paths`.
- Отдельная версия management API (`v2`); проект находится в разработке, внешних потребителей `v1` нет.
- Дедупликация входящих вызовов и распределённая блокировка ожидающих запросов.

### Критерии успеха

- В management API невозможно создать outbound-канал с контрактом или inbound-канал без `swagger.json`.
- `ASYNC` inbound возвращает `202` после успешной отправки, как сейчас.
- `SYNC` inbound возвращает ответ продукта, а при timeout — `504`, при некорректном ответе — `502`.
- Ответ продукта и его заголовки сохраняются в `InboundMessageLog` без отдельной таблицы.
- Продукт получает `reply_expected` и отвечает через `Rebus.Async`.
- Сборка и полный набор тестов проходят; OpenSpec validation успешна.

## Capabilities

### New Capabilities

Нет.

### Modified Capabilities

- `channel-registry`: outbound-канал отделяется от inbound-канала; outbound-канал больше не может владеть OpenAPI-контрактом.
- `contract-registry`: первая версия контракта создаётся вместе с `InboundChannel`; последующие версии по-прежнему загружаются отдельно.
- `inbound-dispatch`: синхронный режим, ожидание ответа, ошибки и журнал ответа.
- `message-contracts`: `reply_expected` в `InboundMessage`, новый `InboundResponse` и правила его полей.
- `management-api`: direction-first ресурсы и полная переработка management-контракта.

## Impact

- `Protos/v1/management_api.proto` и `Protos/v1/integration_messages.proto`.
- `Infrastructure/Persistence`: новые сущности `OutboundChannel`/`InboundChannel`, новые FK, базовая миграция, `InboundMessageLog`.
- `Features/Channels`, `Features/Contracts`, `Features/Inbound`, `Features/Management`, `Features/ProductRoutes`.
- `Infrastructure/Routing` и `Infrastructure/Rebus`: `reply_expected`, request/reply, `Rebus.Async`.
- `Program.cs`, startup secret validation, health API, аудит, README, ARCHITECTURE, ADR и постоянные specs.
- Новый пакет `Rebus.Async` и `EnableSynchronousRequestReply()` на gateway и всех product endpoints.
- Тесты: управление каналами, inbound sync/async, timeout, отмена, ошибки ответа, заголовки, миграции и контракты.
