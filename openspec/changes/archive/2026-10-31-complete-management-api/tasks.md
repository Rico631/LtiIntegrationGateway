# Tasks

## 1. Аудит чтения и отсутствия канала

- [x] 1.1 Расширить `RequireChannelIdAsync` именем операции: при неизвестном канале писать
      отклонённую запись аудита и затем возвращать gRPC `NotFound`.
- [x] 1.2 Передать имена операций из `GetChannel`, `UpdateChannelDraft`,
      `PublishChannelRevision`, `ActivateChannelRevision`, `RollbackChannelRevision`.
- [x] 1.3 Писать аудит успешного чтения в `GetChannel` (операция `GetChannel`, объект — имя канала).

## 2. Аудит отказов валидации настроек версии

- [x] 2.1 В `UpdateContractVersionSettings` фиксировать в аудите отказы, обнаруженные до вызова
      среза: отсутствующие настройки и неизвестный тип авторизации; целевой объект
      `channel:vN`, операция `UpdateContractVersionSettings`.
- [x] 2.2 Убедиться, что срез не пишет вторую запись за тот же отказ.

## 3. Контракт и документация

- [x] 3.1 Обновить комментарий `ChannelSettings.auth_type` в `management_api.proto`: перечислить
      поддерживаемые типы, включая `client_certificate`, и правило обращения с секретом.
- [x] 3.2 Дополнить перечень кодов `OperationProblem.code` фактическими `queue`, `address`,
      `rate_limit`; поля и номера не менять.
- [x] 3.3 Обновить `README.md`: убрать утверждение о нереализованном управляющем API маршрутов
      продуктов, описать аудит чтений и `NotFound` для неизвестного канала.

## 4. Тесты

- [x] 4.1 Тест: успешный `GetChannel` оставляет запись аудита `GetChannel` с результатом `succeeded`.
- [x] 4.2 Тест: `GetChannel` по неизвестному каналу — `404` и запись аудита `rejected`.
- [x] 4.3 Табличный тест: `UpdateChannelDraft`, `PublishChannelRevision`,
      `ActivateChannelRevision`, `RollbackChannelRevision` по неизвестному каналу — `404` и запись
      аудита `rejected` с именем операции.
- [x] 4.4 Тест: `UpdateContractVersionSettings` без настроек и с неизвестным `auth_type` — отказ,
      запись аудита и отсутствие значения секрета в деталях.
- [x] 4.5 Тест: `GetChannelHealth` оставляет ровно одну запись аудита (без дубля из фасада).

## 5. Проверки

- [x] 5.1 Запустить `dotnet build LtiIntegrationGateway.slnx` (0 ошибок и предупреждений),
      `dotnet test LtiIntegrationGateway.slnx`, проверку документации `.proto` из `AGENTS.md`;
      зафиксировать фактические результаты.
- [x] 5.2 Проверить паритет gRPC и REST для новых отказов (`404` в обоих доступах).
- [x] 5.3 Актуализировать `ARCHITECTURE.md`, если меняется описание фасада или аудита.
