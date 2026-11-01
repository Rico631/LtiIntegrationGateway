# Spec Delta

## ADDED Requirements

### Requirement: Входящее сообщение содержит признак ожидаемого ответа

`InboundMessage` SHALL содержать `reply_expected`, вычисленный из режима `InboundChannel`.
Значение SHALL быть `false` для `ASYNC` и `true` для `SYNC`; оно действует одинаково для всех
endpoints канала.

#### Scenario: Асинхронный канал

- **WHEN** `InboundChannel.response_mode` равен `ASYNC`
- **THEN** `reply_expected` в сообщении равен `false`
- **AND** продукт не отправляет `InboundResponse`

#### Scenario: Синхронный канал

- **WHEN** `InboundChannel.response_mode` равен `SYNC`
- **THEN** `reply_expected` в сообщении равен `true`
- **AND** продукт отвечает `InboundResponse` через Rebus

### Requirement: Ответ продукта на синхронный вызов

Продукт SHALL отвечать сообщением `InboundResponse`, содержащим `message_id`, `http_status_code`,
`content_type`, `body`, повторяемые `headers` с полями `name` и `value`, а также `error_code`.
`message_id` SHALL совпадать с `InboundMessage.message_id`.

#### Scenario: Успешный ответ

- **WHEN** продукт возвращает `InboundResponse` с тем же `message_id` и статусом `200`
- **THEN** gateway принимает ответ
- **AND** использует его для формирования HTTP-ответа

#### Scenario: Неверный message_id

- **WHEN** `InboundResponse.message_id` не совпадает с исходным сообщением
- **THEN** ответ считается некорректным
- **AND** gateway не передаёт его клиенту как успешный

### Requirement: Ограничения ответа продукта

`http_status_code` в `InboundResponse` SHALL быть в диапазоне `200..299` или `400..599`.
`1xx` и `3xx` SHALL считаться некорректным ответом. Body MAY отсутствовать; при непустом body
`content_type` SHALL быть задан. `headers` SHALL содержать только разрешённые имена и безопасные
значения.

#### Scenario: Ответ без body

- **WHEN** продукт возвращает статус `204` без body и content-type
- **THEN** ответ принимается
- **AND** gateway возвращает клиенту ответ без тела

#### Scenario: Body без content-type

- **WHEN** продукт возвращает непустое body без `content_type`
- **THEN** ответ считается некорректным
- **AND** gateway возвращает `502 Bad Gateway`

#### Scenario: Запрещённый заголовок

- **WHEN** `InboundResponse.headers` содержит заголовок вне allowlist или значение с CR/LF
- **THEN** весь ответ отклоняется
- **AND** gateway возвращает `502 Bad Gateway`

#### Scenario: error_code не меняет HTTP-статус

- **WHEN** продукт возвращает `http_status_code = 200` и непустой `error_code`
- **THEN** gateway сохраняет `error_code` для аудита
- **AND** клиент получает `200`, а не подменённый статус ошибки
