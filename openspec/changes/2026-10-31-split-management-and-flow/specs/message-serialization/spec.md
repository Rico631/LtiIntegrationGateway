# Spec Delta

## MODIFIED Requirements

### Requirement: Имена очередей шины фиксированы

Шлюз SHALL использовать входную очередь интеграции `lti.gateway.<integration_id>.outbound`,
error-queue интеграции `lti.gateway.<integration_id>.error` и очередь продукта `<product_id>.inbox`.
Имя очереди продукта SHALL вычисляться из `product_id` и SHALL NOT задаваться произвольным значением.
Имя интеграции SHALL соответствовать `^[a-z][a-z0-9-]{1,31}$`; адрес с другим именем SHALL
отклоняться.

#### Scenario: Входная очередь шлюза

- **WHEN** продукт отправляет `OutboundRequest` для интеграции `bank-a`
- **THEN** сообщение принимается из очереди `lti.gateway.bank-a.outbound`
- **AND** экземпляры других интеграций его не получают

#### Scenario: Error-queue

- **WHEN** сообщение интеграции `bank-a` не удалось обработать после всех попыток
- **THEN** оно помещается в очередь `lti.gateway.bank-a.error`
- **AND** не попадает в error-queue другой интеграции

#### Scenario: Количество реплик не влияет на адрес

- **WHEN** число реплик интеграции `bank-a` изменяется
- **THEN** адрес очереди остаётся `lti.gateway.bank-a.outbound`
- **AND** конфигурация продукта не меняется

#### Scenario: Очередь продукта

- **WHEN** шлюз отправляет `OutboundResult` или `InboundMessage` продукту `dpv`
- **THEN** используется очередь `dpv.inbox`

#### Scenario: Некорректное имя интеграции

- **WHEN** адрес очереди содержит имя интеграции вне шаблона `^[a-z][a-z0-9-]{1,31}$`
- **THEN** адрес отклоняется
- **AND** сообщение не отправляется
