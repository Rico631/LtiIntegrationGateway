# Tasks

## 1. Protobuf-контракты и management API

- [x] 1.1 Обновить `integration_messages.proto`: добавить `reply_expected` в `InboundMessage`, `InboundResponse` и `InboundResponseHeader` с обязательной документацией полей; проверить `dotnet build` и документированность `.proto`.
- [x] 1.2 Переписать `management_api.proto` на direction-first ресурсы: outbound/inbound channel messages и RPC, `CreateInboundChannel` с OpenAPI-документом, optional `response_mode`/`inbound_response_timeout_ms`, настройки канала и contract RPC; проверить сборку и документацию.
- [x] 1.3 Добавить/обновить тесты контрактов и mapping для `reply_expected`, `InboundResponse`, optional полей и отклонения `InboundResponseHeader` вне allowlist.

## 2. Схема данных и миграция

- [x] 2.1 Добавить сущности `OutboundChannel` и `InboundChannel`, перенести `ChannelRevision` на outbound, `ContractVersion` — на inbound; удалить общий `Channel` и обновить `GatewayDbContext`.
- [x] 2.2 Расширить `InboundMessageLog` статусами `Replied`/`TimedOut`/`Cancelled`/`Failed` и полями response status/content-type/body/JSON headers; добавить конфигурацию EF.
- [x] 2.3 Удалить старые миграции, создать новую базовую схему и обновить тесты миграций/начального сидирования; проверить `dotnet ef migrations list` и применение к чистой SQLite.
- [x] 2.4 Добавить тесты уникальности имён внутри направления и допустимости одинакового имени в inbound/outbound.

## 3. Outbound-каналы

- [x] 3.1 Разделить `CreateChannel`/`UpdateChannelDraft`/publish/activate/rollback на outbound-канал и его ревизии; создать outbound-канал только с outbound-настройками.
- [x] 3.2 Обновить `ProductRoutes` и проверки отправителя: разрешены только outbound-каналы; inbound-канал отклоняется.
- [x] 3.3 Добавить/обновить тесты outbound-жизненного цикла, product routes, запрета контракта на outbound-канале и публикации без активной ревизии.

## 4. Inbound-каналы и контракты

- [x] 4.1 Реализовать `CreateInboundChannel`: имя, обязательный OpenAPI-документ, optional mode/timeout, создание канала и черновика контракта 1 в одной транзакции.
- [x] 4.2 Реализовать `UpdateInboundChannelSettings` с аудитом, default `ASYNC`/`30000`, изменением mode/timeout и snapshot для текущих ожиданий.
- [x] 4.3 Перенести загрузку последующих версий, publish, activate и чтение операций на `InboundChannel`; outbound-канал не может принимать контракт.
- [x] 4.4 Добавить тесты создания inbound-канала с документом, optional defaults, обновления настроек, версионности, отката и отклонения outbound-канала.

## 5. Синхронный inbound

- [x] 5.1 Подключить `Rebus.Async` в gateway и тестовый product endpoint, включить `EnableSynchronousRequestReply`; проверить request/reply на in-memory шине.
- [x] 5.2 Расширить `IInboundMessagePublisher`/Rebus publisher методом `SendRequest` с destination queue, timeout, cancellation и типом `InboundResponse`.
- [x] 5.3 В `InboundRequestHandler` реализовать channel-level `ASYNC`/`SYNC`, `reply_expected`, ожидание ответа, timeout `504`, отмену клиента и игнорирование позднего ответа.
- [x] 5.4 Реализовать проверку `InboundResponse`: `message_id`, допустимый статус, body/content-type, allowlist заголовков, fail-closed `502` и mapping ответа клиенту.
- [x] 5.5 Обновлять ту же запись `InboundMessageLog` статусами и полями ответа; добавить тесты success, 4xx/5xx, timeout, cancellation, mismatch, invalid status/body/header и late reply.
- [x] 5.6 Обновить inbound-телеметрию: исход sync-ожидания и безопасные теги без body/headers/секретов; проверить тестами.

## 6. Хост, аудит и здоровье

- [x] 6.1 Разделить startup-проверку секретов и health-чтение на outbound/inbound каналы; обновить аудит управляющих операций с направлением.
- [x] 6.2 Обновить регистрацию `Program.cs`, health endpoints и тесты состояния/секретов для двух типов каналов.

## 7. Документация и рабочий процесс

- [x] 7.1 Обновить README, ARCHITECTURE и ADR: физическое разделение каналов, sync/async, `InboundResponse`, timeout и ограничения `Rebus.Async`.
- [x] 7.2 Обновить постоянные specs через этот change и проверить отсутствие старых `/v1/channels` и общего `Channel` в документации.

## 8. Интеграционная проверка

- [x] 8.1 Запустить `dotnet build LtiIntegrationGateway.slnx` и полный `dotnet test`, зафиксировать реальные результаты.
- [x] 8.2 Запустить `openspec validate 2026-10-31-sync-inbound-request-reply --strict` и проверку документации `.proto` по `AGENTS.md`.
- [x] 8.3 Проверить end-to-end сценарий на тестовом product endpoint: async `202`, sync `2xx`, product `4xx/5xx`, timeout `504`, invalid response `502`.
