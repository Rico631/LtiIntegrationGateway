# message-contracts Specification

## Purpose

Контракты обмена сообщениями между продуктами и шлюзом через Rebus: состав и обязательность
полей, представление тела и query-параметров, профиль кодирования на проводе и правила
совместимости, на которые опираются продукты, генерирующие типы из `.proto`.

## Requirements

### Requirement: Обязательные поля исходящего запроса

`OutboundRequest` SHALL содержать `message_id`, `product_id`, `channel_name`, `relative_path` и
`http_method`. Отсутствие любого из них SHALL делать сообщение невалидным: внешний вызов не
выполняется. Повторная отправка с тем же `message_id` SHALL обозначать ту же операцию.

#### Scenario: Полный запрос

- **WHEN** продукт отправляет `OutboundRequest` со всеми обязательными полями
- **THEN** сообщение считается валидным и передаётся в обработку
- **AND** `message_id` идентифицирует операцию

#### Scenario: Отсутствует идентификатор продукта

- **WHEN** в `OutboundRequest` не задан `product_id`
- **THEN** сообщение отклоняется как невалидное
- **AND** внешний вызов не выполняется

#### Scenario: Повторная отправка с тем же message_id

- **WHEN** продукт повторно отправляет запрос с уже использованным `message_id`
- **THEN** это трактуется как та же операция, а не как новая

### Requirement: Тело передаётся байтами

Тело HTTP-запроса и ответа SHALL передаваться как `bytes` в исходном виде, без нормализации и
без преобразования в объектную модель. `content_type` SHALL быть задан при непустом теле.

#### Scenario: Тело сохраняется без искажений

- **WHEN** сообщение с JSON-телом, содержащим числа и вложенные объекты, кодируется и
  разбирается заново
- **THEN** тело совпадает с исходным байт в байт
- **AND** исходное представление чисел не меняется

#### Scenario: Тело без типа содержимого

- **WHEN** тело задано, а `content_type` пуст
- **THEN** сообщение считается невалидным

### Requirement: Query-параметры сохраняют порядок и повторы

`query_parameters` SHALL быть упорядоченной последовательностью пар `name` и `value`. Повторяющиеся
имена параметров SHALL сохраняться, порядок SHALL сохраняться. Тип-словарь SHALL не использоваться.

#### Scenario: Повторяющиеся параметры

- **WHEN** запрос содержит `?account=1&account=2`
- **THEN** сообщение содержит два элемента `query_parameters` в исходном порядке

#### Scenario: Пустой список параметров

- **WHEN** у запроса нет query-параметров
- **THEN** список `query_parameters` пуст и это не является ошибкой

### Requirement: Результат различает исход вызова

`OutboundResult` SHALL содержать `outcome` со значениями `HTTP_RESPONSE`, `TRANSPORT_ERROR`,
`GATEWAY_REFUSED`. При полученном HTTP-ответе SHALL передаваться фактический статус и тело. При
отсутствии ответа статус SHALL отсутствовать, а `error_code` SHALL быть задан.

#### Scenario: Внешняя система вернула ошибку

- **WHEN** внешний сервис отвечает `500` с телом
- **THEN** `outcome` = `HTTP_RESPONSE`, статус = `500`, тело передаётся продукту

#### Scenario: Ответ не получен

- **WHEN** вызов завершился таймаутом
- **THEN** `outcome` = `TRANSPORT_ERROR`, `error_code` задан, HTTP-статус отсутствует
- **AND** продукт может отличить это от полученного ответа `500`

#### Scenario: Отказ шлюза

- **WHEN** шлюз отказал до внешнего вызова (недопустимый путь, неизвестный продукт)
- **THEN** `outcome` = `GATEWAY_REFUSED`, `error_code` содержит причину отказа

### Requirement: Входящее сообщение содержит фактический маршрут

`InboundMessage` SHALL содержать `http_method`, `relative_path`, `route_template` и `body`.
Параметры пути отдельным полем не передаются. `relative_path` SHALL передаваться без query
string и без percent-декодирования.

#### Scenario: Путь соответствует шаблону

- **WHEN** внешняя система вызывает `/callbacks/dkp-status`
- **THEN** `relative_path` и `route_template` равны `/callbacks/dkp-status`

#### Scenario: Путь содержит параметр

- **WHEN** внешняя система вызывает `/callbacks/contracts/123/status` для шаблона
  `/callbacks/contracts/{id}/status`
- **THEN** `relative_path` содержит фактический путь
- **AND** `route_template` содержит шаблон из контракта

#### Scenario: Query-параметры не входят в путь

- **WHEN** запрос вызван как `/callbacks/dkp-status?source=bank`
- **THEN** `relative_path` не содержит `?source=bank`
- **AND** параметр `source` присутствует в `query_parameters`

### Requirement: Профиль кодирования на проводе

Сообщения SHALL кодироваться как ProtoJSON: имена полей в camelCase, `bytes` в base64, enum
именем, 64-битные целые строкой. Заголовок `rbs2-msg-type` SHALL содержать полное имя сообщения.
Неизвестные поля SHALL игнорироваться при разборе.

#### Scenario: Round-trip сообщения

- **WHEN** сообщение сериализуется и разбирается заново по профилю
- **THEN** значения всех полей сохраняются, включая тело в base64

#### Scenario: Неизвестное поле в сообщении

- **WHEN** присланное сообщение содержит поле, которого нет в текущей версии `.proto`
- **THEN** разбор не завершается ошибкой
- **AND** известные поля заполняются корректно

#### Scenario: Определение типа сообщения

- **WHEN** сообщение передаётся через Rebus
- **THEN** заголовок `rbs2-msg-type` содержит полное имя сообщения, например
  `lti.gateway.messages.v1.OutboundRequest`

### Requirement: Правила совместимости контракта

Номера полей SHALL не переиспользоваться. Удалённые поля SHALL помечаться `reserved` по номеру и
имени. Скаляры, где различие «не задано» и значения по умолчанию существенно, SHALL быть
`optional`. Enum SHALL использоваться только для закрытых множеств. `google.protobuf.Any` SHALL
быть запрещён в интеграционных сообщениях.

#### Scenario: Необязательные поля различимы

- **WHEN** `idempotency_key`, `error_code` или `content_type` не заданы
- **THEN** поле отсутствует в сообщении, а не приходит со значением по умолчанию

#### Scenario: Запрет Any

- **WHEN** в контракте появляется поле типа `google.protobuf.Any`
- **THEN** контракт считается нарушающим требования и не принимается

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
