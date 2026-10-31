# Design

## 1. Доставка отказа

`OutboundResult` публикуется только на адрес, который прошёл проверку по `ProductRoutes`.
Действующий адрес результата — `QueueNames.ProductInbox(product_id)`; он же проверяется для
`rbs2-sender-address` и `rbs2-return-address`.

Новый порядок в `ProcessOutboundRequestHandler`:

```text
1. Обязательные поля DTO. Нарушение -> FailFastException (см. §2), без журнала.
2. Операция по message_id; чужой product_id -> отказ PRODUCT_NOT_ALLOWED.
3. Проверка product_id/sender address по ProductRoutes.
4. Проверка return address: только вычисленный inbox.
5. Завершённая операция -> сохранённый результат (только на проверенный адрес).
6. Остальные проверки канала/метода/пути/авторизации.
7. Отказ -> операция Completed + журнал; доставка только при проверенном адресе.
```

Если продукт/отправитель/адрес не подтверждены, отказ фиксируется в операции и журнале, но
`PublishOutboundResultHandler` не вызывается. Для этого в `OutboundDeliveryStatus` добавляется
`Undeliverable = 3`; диспетчер выбирает только `Pending`, поэтому запись не берётся повторно.
`OutboundMessageLog.DeliveryAddress` для недоставляемого отказа — пустая строка.

## 2. Невалидный DTO и error-queue

`MessageContractValidator.ValidateOutboundRequest` уже возвращает список пустых полей. В
`OutboundRequestHandler` до вызова сценария:

```csharp
if (problems.Count > 0)
{
    throw new FailFastException(
        $"OutboundRequest: нарушены обязательные поля: {string.Join(", ", problems)}");
}
```

Rebus 8 обрабатывает `FailFastException` без повторов и помещает сообщение в `lti.gateway.error`.
Журнал и операция не создаются: у сообщения нет валидного бизнес-ключа, а `message_id` может быть
пустым. Значения полей в тексте ошибки не раскрываются — только имена.

## 3. Обязательные поля management-команд

`MessageContractValidator` расширяется методами для management-запросов. Проверяются только
команды, у ответа которых есть `problems` и `succeeded`:

| Команда | Обязательные поля |
|---|---|
| `CreateChannel` | `name`, `settings` |
| `UpdateChannelDraft` | `channel_name`, `settings` |
| `PublishChannelRevision`, `ActivateChannelRevision` | `channel_name`, `revision > 0` |
| `RollbackChannelRevision` | `channel_name` |
| `RegisterProductRoute` | `product_id`, `allowed_channels` |
| `UploadContract` | `channel_name`, `document` |
| `PublishContractVersion`, `ActivateContractVersion` | `channel_name`, `version > 0` |
| `UpdateContractVersionSettings` | `channel_name`, `version > 0`, `settings` |
| `UpdateContractOperationSettings` | `channel_name`, `version > 0`, `operation_id` |

Отказ возвращается как `problems` с `severity = ERROR`, `succeeded = false`, статус 200. Методы
чтения не меняются: пустое имя/версия приводят к `NotFound` через существующие проверки.

## 4. Severity и предупреждения

`OperationProblem` получает поле:

```proto
enum ProblemSeverity {
  PROBLEM_SEVERITY_UNSPECIFIED = 0;
  PROBLEM_SEVERITY_ERROR = 1;
  PROBLEM_SEVERITY_WARNING = 2;
}

message OperationProblem {
  string code = 1;
  string message = 2;
  ProblemSeverity severity = 3;
}
```

Правила:

- все существующие отказы получают `ERROR`;
- `ContractOperationResponse.warnings` удаляется; предупреждения OpenAPI (`format` и т. п.)
  конвертируются в `OperationProblem` с `severity = WARNING`;
- публикация канала без секрета добавляет `OperationProblem { code = "secret_key_missing",
  severity = WARNING }` в `problems`, `succeeded = true`.

`ManagementApiService.ToProblem` заполняет severity; для warnings используется `WARNING`.

## 5. Проверка секретов при старте

После `Database.Migrate()` и до `app.Run()`:

```text
missing = []
для каждой Active ревизии канала:
    если auth_type требует секрет:
        ISecretStore.Find(config_key) пуст -> missing.Add($"channel {name}: {config_key}")
        client_certificate: IChannelCertificateStore.Load -> null -> missing
для каждой Active версии контракта:
    аналогично по auth_type/config_key
если missing не пуст:
    залогировать каждую строку как Error
    throw InvalidOperationException со списком ключей (без значений)
```

Проверяются только `Active`; черновики и опубликованные не блокируют старт. Компонент —
`Features/Host/StartupSecretValidator`, вызывается из `Program.cs`.

## 6. Удаление квоты операции

Удаляются:

- `ContractOperation.RateLimitPermit`, `ContractOperation.RateLimitWindowSeconds`;
- поля 6 и 7 из `ContractOperationInfo` и `UpdateContractOperationSettingsRequest`;
- поля из `ContractOperationSettingsPatch`, `ContractOperationDetails`, маппинга и валидации;
- `ContractOperationSettings` больше не резолвит квоту операции — только версию/приложение.

Миграция `RemoveContractOperationRateLimit` удаляет колонки. Лимитер продолжает использовать ключ
`channel|permit|window`; после удаления per-op квоты значения одинаковы для всех операций канала,
поэтому бюджет канала не умножается. Это документируется как «ключ — канал, значения входят в ключ
партиции для сброса окна при смене квоты».

## 7. Удаление Unix-колонок

`CreatedAtUnixMs` и `ReceivedAtUnixMs` удаляются. Для `OutboundMessageLog.CreatedAt` и
`InboundMessageLog.ReceivedAt` настраивается EF-конвертер:

```csharp
log.Property(entity => entity.CreatedAt)
   .HasConversion(
       value => value.ToUnixTimeMilliseconds(),
       value => DateTimeOffset.FromUnixTimeMilliseconds(value));
```

После этого SQLite сортирует по целочисленной колонке, и запросы используют
`OrderBy(log => log.CreatedAt)` / `OrderBy(log => log.ReceivedAt)`. Индексы пересоздаются на эти
колонки. Миграция `RemoveUnixTimestampColumns` пересобирает таблицы (SQLite не умеет `ALTER COLUMN`).

## 8. Влияние на контракты и совместимость

Сервис в разработке: удаление полей и `warnings` допускается без `reserved`. Потребители
управляющего API должны перейти на `problems[].severity`. Rebus-контракты `OutboundRequest`/
`OutboundResult` не меняются.

## 9. ADR

Обновляются §7.3/§13/§18/решение 27: квота задаётся только версией контракта, ключ — канал.
§7.5: отсутствие секрета активной конфигурации завершает запуск. §15.3: предупреждение публикации
передаётся в `problems` с `severity`.

## 10. Тесты

- отказ не публикуется на непроверенный адрес; журнал `Undeliverable`; существующий тест меняет
  ожидание;
- невалидный DTO → `lti.gateway.error`, журнал пуст, повторов нет;
- management-команда с пустым полем → `problems` `ERROR`, `succeeded=false`; чтение → `NotFound`;
- предупреждение публикации канала → `WARNING`, `succeeded=true`;
- старт без секрета → исключение с именем ключа; старт с секретом → успешен;
- удаление квоты операции: поля отсутствуют в API/БД, лимит канала общий;
- `CreatedAt`/`ReceivedAt` сортируются через конвертер, колонок `*UnixMs` нет.

