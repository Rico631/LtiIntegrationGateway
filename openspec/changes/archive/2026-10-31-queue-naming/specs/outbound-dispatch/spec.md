# Spec Delta

## ADDED Requirements

### Requirement: Адрес очереди продукта вычисляется из product_id

Шлюз SHALL вычислять адрес очереди продукта как `<product_id>.inbox`. Регистрация маршрута продукта
SHALL принимать пустой список адресов (вычисленный адрес) или адрес, совпадающий с вычисленным;
любой другой адрес SHALL отклоняться. `rbs2-sender-address` и `rbs2-return-address` SHALL
сравниваться с вычисленным адресом продукта.

#### Scenario: Регистрация без адреса

- **WHEN** для продукта `dpv` регистрируется маршрут без списка адресов
- **THEN** сохраняется адрес `dpv.inbox`
- **AND** чтение маршрута возвращает `dpv.inbox`

#### Scenario: Произвольный адрес при регистрации

- **WHEN** для продукта `dpv` передаётся адрес `other.queue`
- **THEN** регистрация отклоняется с проблемой `address`
- **AND** существующий маршрут не меняется

#### Scenario: Адрес отправителя не совпадает

- **WHEN** `rbs2-sender-address` не равен `<product_id>.inbox`
- **THEN** результат содержит `PRODUCT_NOT_ALLOWED`
- **AND** HTTP-вызов не выполняется

#### Scenario: Адрес результата не совпадает

- **WHEN** `rbs2-return-address` не равен `<product_id>.inbox`
- **THEN** результат содержит `PRODUCT_NOT_ALLOWED`
- **AND** HTTP-вызов не выполняется
