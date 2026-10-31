# Tasks

## 1. Доставка отказа

- [x] 1.1 Добавить `Undeliverable = 3` в `OutboundDeliveryStatus`; диспетчер продолжает выбирать
      только `Pending` с непустым адресом и существующей операцией.
- [x] 1.2 В `ProcessOutboundRequestHandler` публиковать отказ только при проверенном адресе
      результата; иначе операция `Completed`, журнал `Undeliverable`, пустой `DeliveryAddress`.
- [x] 1.3 Обновить тест `Return_address_not_equal_to_computed_inbox_is_refused`: результат не
      отправляется, журнал `Undeliverable`; добавить тест неизвестного продукта.

## 2. Невалидный OutboundRequest

- [x] 2.1 В `OutboundRequestHandler` проверять обязательные поля до сценария и бросать
      `FailFastException`; сообщение попадает в `lti.gateway.error` без повторов.
- [x] 2.2 Тест: невалидный DTO — в error-queue, журнал пуст, HTTP-вызов не выполняется.

## 3. Severity, предупреждения и валидация команд

- [x] 3.1 Добавить `ProblemSeverity` (`UNSPECIFIED`, `ERROR`, `WARNING`) и поле `severity` в
      `OperationProblem`; заполнять `ERROR` для существующих отказов.
- [x] 3.2 Удалить `ContractOperationResponse.warnings`; предупреждения OpenAPI передавать как
      `OperationProblem` с `WARNING`.
- [x] 3.3 Расширить `MessageContractValidator` на management-команды из design.md; в фасаде
      возвращать `problems` `ERROR` и `succeeded=false` до вызова среза.
- [x] 3.4 При публикации канала проверять секрет/сертификат и добавлять
      `secret_key_missing` с `WARNING` при успехе.
- [x] 3.5 Тесты: пустое поле команды; предупреждение публикации; отсутствие `warnings` в контракте.

## 4. Секреты при старте

- [x] 4.1 Добавить `StartupSecretValidator`: активные ревизии каналов и версии контрактов, проверка
      `ISecretStore`/`IChannelCertificateStore`; собрать все missing.
- [x] 4.2 Вызвать после миграций в `Program.cs`; при missing — ошибка запуска с именами ключей без
      значений.
- [x] 4.3 Тесты: старт без секрета падает; с секретом — успешен; черновик не блокирует.

## 5. Удаление квоты операции

- [x] 5.1 Удалить `RateLimitPermit`/`RateLimitWindowSeconds` из `ContractOperation`, настроек
      операции, маппинга и валидации; миграция `RemoveContractOperationRateLimit`.
- [x] 5.2 Удалить поля 6 и 7 из `ContractOperationInfo` и
      `UpdateContractOperationSettingsRequest`; обновить комментарии.
- [x] 5.3 Обновить `ContractOperationSettings`: квота только версии/приложения; лимитер — общий
      бюджет канала.
- [x] 5.4 Обновить/удалить тесты переопределения квоты; добавить тест общего бюджета канала.

## 6. Удаление Unix-колонок

- [x] 6.1 Настроить EF-конвертер `DateTimeOffset -> long` для `OutboundMessageLog.CreatedAt` и
      `InboundMessageLog.ReceivedAt`.
- [x] 6.2 Удалить `CreatedAtUnixMs`/`ReceivedAtUnixMs` из сущностей, писателей, запросов и индексов;
      миграция `RemoveUnixTimestampColumns`.
- [x] 6.3 Обновить health/dispatcher/тесты на `OrderBy(CreatedAt/ReceivedAt)`; проверить сортировку
      на SQLite.

## 7. Документация и проверки

- [x] 7.1 Обновить ADR §7.3, §7.5, §13, §15.3, §18 и решение 27; README и ARCHITECTURE.
- [x] 7.2 Запустить `dotnet build` (0/0), `dotnet test`, `openspec validate --strict`, проверку
      `.proto` по `AGENTS.md`; зафиксировать результаты.
