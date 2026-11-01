# Tasks

## 1. Общая инфраструктура шины

- [x] 1.1 Создать `src/LtiIntegrationGateway.Messaging` (ссылки: `Contracts`, `Google.Protobuf`, `Rebus`), перенести `ProtobufSerializer` и `ProtobufMessageRegistry`, namespace `LtiIntegrationGateway.Messaging`
- [x] 1.2 Переключить `Flow` на новый проект, удалить перенесённые файлы из `Flow/Infrastructure/Rebus`, обновить ссылки в тестах; сборка и существующие тесты сериализации и шины — зелёные
- [x] 1.3 Подключить `Messaging` к `LtiIntegrationGateway.slnx`

## 2. Контракты симуляции

- [x] 2.1 Добавить `simulation/contracts/product-orders-async.json` (OpenAPI 3.0, `POST /order`) и `simulation/contracts/product-orders-sync.json` (`GET /order/{orderid}`); оба проходят загрузку, публикацию и активацию в шлюзе
- [x] 2.2 Встроить контракты в сборку продукта (`EmbeddedResource`) и отдавать их по `/contracts/{name}.json`, чтобы Swagger показывал ровно те документы, что загружены в шлюз

## 3. Сервис product

- [x] 3.1 Создать `simulation/LtiIntegrationGateway.Simulation.Product`: Rebus на PostgreSQL (очередь `product.inbox`, общий `ProtobufSerializer`), `AddGatewayObservability`, конфигурация `Gateway:*`, `Management:*`, `Integration:*`
- [x] 3.2 Реализовать `ManagementClient` + `ManagementBootstrapHostedService`: идемпотентно создать и активировать канал `http`, каналы `product-orders-async`/`product-orders-sync` с контрактами и маршрут `product`, дождаться `flow_instances.up_to_date`
- [x] 3.3 Реализовать `OutboundLoopService`: раз в 5 секунд публиковать `OutboundRequest` `POST /customer` с `rbs2-return-address = product.inbox`
- [x] 3.4 Реализовать обработчики: `OutboundResultHandler` логирует ответ внешней системы, `InboundOrderHandler` логирует входящий вызов и при `reply_expected = true` отвечает `InboundResponse` с сохранением `rbs2-corr-id`
- [x] 3.5 Подключить `/health/live` и `/health/ready`: готовность наступает после регистрации конфигурации

## 4. Сервис integration

- [x] 4.1 Создать `simulation/LtiIntegrationGateway.Simulation.Integration` с операцией `POST /customer`, возвращающей `{status: ok, message: "recieved <name>"}`
- [x] 4.2 Реализовать `IntegrationLoopService`: раз в 5 секунд `POST /order` и `GET /order/{orderid}` к `flow-bank-a`, логировать статус и тело ответа, ошибки транспорта не останавливают цикл

## 5. Локальный контур

- [x] 5.1 Подключить оба сервиса в `AppHost`: ссылки на PostgreSQL, фиксированные порты, переменные окружения (интеграция, продукт, адрес управляющего API и ключ, адрес внешней системы, адрес `flow-bank-a`)
- [x] 5.2 Задать порядок запуска: `management` (health `/health/ready`) → `flow-bank-a`/`flow-bank-b` (health, `WaitFor(management)`) → `product` (health, `WaitFor` обоих flow) → `integration` (`WaitFor(product)`)

## 6. Отказ от скриптов

- [x] 6.1 Удалить `simulation/bootstrap.ps1` и `simulation/scenario.ps1`
- [x] 6.2 Удалить эндпоинты `/simulation/*` из продукта и внешней системы; состояние наружу не отдаётся, единственный интерфейс — логи и Swagger

## 7. Документация

- [x] 7.1 Обновить `AGENTS.md` и `ARCHITECTURE.md` (новый проект `Messaging`, проекты симуляции, dev-only назначение, отсутствие скриптов) и `README.md` (порядок запуска, три сценария обмена, адреса Swagger)
- [x] 7.2 Обновить `proposal.md`, `design.md` (решения D3–D10, D12) и `tasks.md` под фактическую реализацию

## 8. Проверка

- [x] 8.1 `dotnet build LtiIntegrationGateway.slnx` без предупреждений и ошибок; тесты `Core` (56), `Management` (139), `Flow` (168) — зелёные
- [x] 8.2 Живой прогон AppHost: порядок запуска соблюдается (`management` → `flow-*` → `product` → `integration`), продукт сам создаёт и повторно применяет конфигурацию
- [x] 8.3 Проверить три сценария по журналам шлюза: `OutboundMessageLogs` — `POST /customer` с 200 и телом `{"status":"ok","message":"recieved customer-N"}`; `InboundMessageLogs` — `product-orders-async` в статусе `Sent`, `product-orders-sync` в статусе `Replied` с телом заказа
- [x] 8.4 `openspec validate 2026-11-01-debug-simulation` — без замечаний

## 9. Swagger в сервисах симуляции

- [x] 9.1 Включить OpenAPI-документ и Swagger UI в `Simulation.Product` и `Simulation.Integration` (`/openapi/v1.json`, `/swagger`), аннотировать эндпоинты русскими summary
- [x] 9.2 Опустить `Microsoft.OpenApi` до 2.12.2 (штатный генератор несовместим с 3.x) и подтвердить тестами `Core` и `Management`, что чтение и проверка контрактов не изменились
- [x] 9.3 В Swagger продукта добавить оба контракта inbound-каналов (`/contracts/product-orders-async.json`, `/contracts/product-orders-sync.json`)
- [x] 9.4 В `flow` отдавать сводный OpenAPI активных входящих контрактов (`/openapi/inbound.json`) и показывать его в Swagger UI (`/swagger`)
- [x] 9.5 Подавить info-логи опроса `/health/*` (`Hosting.Diagnostics`, `Routing.EndpointMiddleware`) и открывать из dashboard ссылки на Swagger (`WithUrlForEndpoint`)
- [x] 9.6 Поднимать `pgweb` вместе с PostgreSQL в AppHost (`http://localhost:8081`) для просмотра записей БД
- [x] 9.7 Сделать `RegisterProductRoute` идемпотентным: дочерние строки маршрута приводятся точечно, повторная регистрация не меняет данные и версию конфигурации; новые строки создаются без явного ключа (иначе EF делает `UPDATE` вместо `INSERT`); регрессионные тесты на повтор и на смену набора каналов
- [x] 9.8 Переименовать outbound-канал `bank-a-http` → `http`: имя уникально внутри интеграции, префикс дублировал владельца

