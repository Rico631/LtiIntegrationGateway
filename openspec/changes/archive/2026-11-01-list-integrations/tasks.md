# Tasks

## 1. Контракт

- [x] 1.1 Добавить в `protos/v1/management_api.proto` rpc `ListIntegrations` с аннотацией `google.api.http` `get: "/v1/integrations"` и сообщения `ListIntegrationsRequest`, `IntegrationInfo`, `ListIntegrationsResponse`; каждому rpc, сообщению и полю — комментарий по правилам `AGENTS.md`
- [x] 1.2 Проверить, что номера и типы существующих полей не менялись, `package lti.gateway.management.v1` сохранён, и прогнать проверку документации `.proto` скриптом из `AGENTS.md`

## 2. Реализация

- [x] 2.1 Реализовать `ListIntegrations` в `ManagementApiService` отдельным partial-файлом `ManagementApiService.Integrations.cs`: `db.Integrations.AsNoTracking()` с сортировкой по `IntegrationId` и маппингом в `IntegrationInfo` (`integration`, `configuration_version`, `created_at`)
- [x] 2.2 Убедиться, что метод ничего не пишет в аудит и не создаёт записей в `Integrations` (чтение не вызывает `IntegrationRegistry`)

## 3. Тесты

- [x] 3.1 Тест: на базе без настроенных интеграций `GET /v1/integrations` возвращает успешный ответ с пустым списком
- [x] 3.2 Тест: после создания каналов в `bank-a` и `bank-b` ответ содержит обе записи в порядке идентификаторов, `configuration_version` соответствует версии после настройки, `created_at` заполнен; настройка без явной адресации даёт запись `default`
- [x] 3.3 Тест: REST `GET /v1/integrations` и gRPC `ListIntegrations` дают одинаковый состав данных; запрос без ключа API отклоняется
- [x] 3.4 Тест: вызов списка не добавляет записей в `ManagementAuditEntries`

## 4. Проверка

- [x] 4.1 Собрать решение `dotnet build LtiIntegrationGateway.slnx` без предупреждений и ошибок и прогнать тесты `LtiIntegrationGateway.Management.Tests`
- [x] 4.2 Убедиться, что метод присутствует в `/openapi/management.json` и `/swagger` (документ строится из дескриптора; существующий тест сверяет число операций с числом rpc)
