# Spec Delta

## ADDED Requirements

### Requirement: Имена очередей шины фиксированы

Шлюз SHALL использовать входную очередь `lti.gateway.outbound`, error-queue `lti.gateway.error` и
очередь продукта `<product_id>.inbox`. Имя очереди продукта SHALL вычисляться из `product_id` и
SHALL NOT задаваться произвольным значением.

#### Scenario: Входная очередь шлюза

- **WHEN** продукт отправляет `OutboundRequest`
- **THEN** сообщение принимается из очереди `lti.gateway.outbound`

#### Scenario: Error-queue

- **WHEN** сообщение не удалось обработать после всех попыток
- **THEN** оно помещается в очередь `lti.gateway.error`, а не в очередь с суффиксом `.error` от
  входной очереди

#### Scenario: Очередь продукта

- **WHEN** шлюз отправляет `OutboundResult` или `InboundMessage` продукту `dpv`
- **THEN** используется очередь `dpv.inbox`
