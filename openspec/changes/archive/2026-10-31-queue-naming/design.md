# Design

## Контекст

ADR §10.2 и решение 28 фиксируют три имени очередей. Сейчас `RebusConfigurationExtensions` задаёт
только входную очередь, error-queue остаётся производной Rebus (`lti.gateway.outbound.error`), а
адреса продукта хранятся списком произвольных строк (`ProductRouteQueueAddress`).

## Имена очередей

Новый статический класс `Infrastructure/Rebus/QueueNames`:

```text
OutboundQueue = "lti.gateway.outbound"
ErrorQueue    = "lti.gateway.error"
ProductInbox(productId) = $"{productId}.inbox"
```

- `RebusConfigurationExtensions` использует `QueueNames.OutboundQueue` и настраивает error-queue:
  `.Options(o => o.RetryStrategy(errorQueueName: QueueNames.ErrorQueue))` с сохранением значений по
  умолчанию для числа попыток и кулдауна;
- имя очереди продукта не хранится как конфигурация: оно всегда вычисляется из `product_id`.

## Маршрут продукта

| Операция | Поведение |
|---|---|
| `RegisterProductRoute` без адресов | сохраняется один адрес `ProductInbox(productId)` |
| `RegisterProductRoute` с адресом, равным вычисленному | сохраняется (идемпотентно) |
| `RegisterProductRoute` с другим адресом | проблема `address`, маршрут не меняется |
| `GetProductRoute` | возвращает вычисленный адрес |

Существующие записи с произвольными адресами не удаляются и не переписываются миграцией: при
проверке вызовов список адресов больше не используется, сравнение идёт с вычисленным адресом.
Повторная регистрация маршрута приводит запись к вычисленному адресу.

## Проверка отправителя и результата

`ResolveSenderIdentityHandler`:

```text
route = ProductRoutes по product_id (Include AllowedChannels)
route отсутствует            -> PRODUCT_NOT_ALLOWED
senderAddress != ProductInbox(product_id) -> PRODUCT_NOT_ALLOWED
channel не разрешён          -> CHANNEL_NOT_ALLOWED
AllowedAddresses = [ProductInbox(product_id)]
```

`ProcessOutboundRequestHandler` проверяет `rbs2-return-address` по этому же единственному адресу;
отсутствие или несовпадение — `PRODUCT_NOT_ALLOWED` без HTTP-вызова. Список
`ProductRouteQueueAddress` остаётся в модели для обратной совместимости чтения, но источником истины
не является.

## Очередь получателя контракта

При публикации версии `RecipientQueue` проверяется по шаблону
`^[a-z][a-z0-9-]{1,31}\.inbox$` (тот же формат `product_id`, что в `OutboundRequest`). Несоответствие
— проблема `queue`, операции не создаются. Проверка выполняется вместе с существующей обязательностью
очереди, до чтения документа.

## Тестируемость

- unit-тест `QueueNames.ProductInbox`;
- интеграционный тест error-queue: обработчик тестовой шины бросает исключение, после исчерпания
  попыток сообщение оказывается в очереди `lti.gateway.error` (in-memory network Rebus);
- тесты регистрации: без адреса, совпадающий адрес, несовпадающий адрес (проблема `address`);
- тесты исходящего контура: `rbs2-sender-address` и `rbs2-return-address` не равны вычисленному —
  `PRODUCT_NOT_ALLOWED`, внешний вызов не выполняется;
- тесты публикации: очередь `dpv.inbox` проходит, `inbound.queue` отклоняется проблемой `queue`.

## Влияние на контракты

Protobuf-контракты не меняются: поле `allowed_queue_addresses` в `RegisterProductRouteRequest`
сохраняется для совместимости, но принимает только вычисленное значение; ответ чтения возвращает
вычисленный адрес. Поведение ужесточается, что фиксируется в README.

## Отклонения от ADR

Нет. Реализуется §10.2 и решение 28; контроль доступа к очередям на стороне брокера остаётся
требованием к промышленному транспорту (ADR §20).
