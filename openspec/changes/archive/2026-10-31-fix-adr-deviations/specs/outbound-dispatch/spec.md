# Spec Delta

## ADDED Requirements

### Requirement: Отказ не доставляется на непроверенный адрес

Шлюз SHALL публиковать `OutboundResult` только на адрес, проверенный по конфигурации `ProductRoutes`
(вычисленный `<product_id>.inbox`). Если продукт, адрес отправителя или адрес результата не
подтверждены, операция SHALL завершаться, запись журнала SHALL получать статус `Undeliverable`,
а результат SHALL NOT отправляться.

#### Scenario: Непроверенный адрес результата

- **WHEN** `rbs2-return-address` не равен вычисленному адресу продукта
- **THEN** результат содержит `PRODUCT_NOT_ALLOWED`
- **AND** HTTP-вызов не выполняется
- **AND** результат не публикуется
- **AND** запись журнала имеет статус `Undeliverable`

#### Scenario: Неизвестный продукт

- **WHEN** `product_id` отсутствует в `ProductRoutes`
- **THEN** результат не публикуется
- **AND** запись журнала имеет статус `Undeliverable`

#### Scenario: Проверенный адрес

- **WHEN** продукт и адрес результата подтверждены
- **THEN** результат публикуется в очередь продукта
- **AND** запись журнала получает статус `Sent` после успешной отправки

### Requirement: Невалидный OutboundRequest уходит в error-queue

`OutboundRequest` с пустым обязательным полем (`message_id`, `product_id`, `channel_name`,
`relative_path`, `http_method`, либо непустое тело без `content_type`) SHALL NOT журналироваться и
SHALL NOT обрабатываться: сообщение SHALL немедленно попадать в `lti.gateway.error` без повторных
попыток.

#### Scenario: Пустое обязательное поле

- **WHEN** получен `OutboundRequest` без `message_id`
- **THEN** сообщение попадает в `lti.gateway.error`
- **AND** запись в журнале сообщений не создаётся
- **AND** внешний HTTP-вызов не выполняется

#### Scenario: Нет повторов

- **WHEN** сообщение отклонено проверкой обязательных полей
- **THEN** повторные попытки обработки не выполняются
