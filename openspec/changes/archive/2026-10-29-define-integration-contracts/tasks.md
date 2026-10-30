# Tasks

## 1. Контракт `.proto`

- [x] 1.1 Создать `src/LtiIntegrationGateway/Protos/v1/integration_messages.proto`:
      `syntax = "proto3"`, `package lti.gateway.messages.v1`, `csharp_namespace
      LtiIntegrationGateway.Contracts.Messages.V1`
- [x] 1.2 Записать в шапку файла нормативный профиль кодирования (ProtoJSON, camelCase,
      `bytes` → base64, `int64` → строка, enum именем, неизвестные поля игнорируются,
      `rbs2-msg-type` = полное имя сообщения) и запрет `google.protobuf.Any`
- [x] 1.3 Определить `QueryParameter` (`name`, `value`) и `enum Outcome` со значениями
      `OUTCOME_UNSPECIFIED = 0`, `HTTP_RESPONSE`, `TRANSPORT_ERROR`, `GATEWAY_REFUSED`
- [x] 1.4 Определить `OutboundRequest` по таблице ADR §8.3: `message_id`, `product_id`,
      `channel_name`, `relative_path`, `http_method`, `optional content_type`, `bytes body`,
      `repeated QueryParameter query_parameters`, `optional correlation_id`,
      `optional idempotency_key`
- [x] 1.5 Определить `OutboundResult` по ADR §8.5: `message_id`, `product_id`, `channel_name`,
      `Outcome outcome`, `optional int32 http_status_code`, `optional content_type`,
      `bytes response_body`, `optional error_code`, `optional error_message`,
      `optional correlation_id`, `int32 attempt_count`
- [x] 1.6 Определить `InboundMessage` по ADR §9.3: `message_id`, `channel_name`, `http_method`,
      `relative_path`, `route_template`, `optional operation_id`, `optional content_type`,
      `bytes body`, `repeated QueryParameter query_parameters`, `google.protobuf.Timestamp
      received_at`, `optional external_message_id`, `optional correlation_id`; поля
      `path_parameters` нет

## 2. Генерация C#-типов

- [x] 2.1 Добавить в `LtiIntegrationGateway.csproj` пакеты `Google.Protobuf` 3.37.0 и
      `Grpc.Tools` 2.85.0 с `PrivateAssets="All"`
- [x] 2.2 Добавить элемент `<Protobuf Include="Protos\v1\integration_messages.proto"
      GrpcServices="None" />`
- [x] 2.3 Выполнить `dotnet build` и убедиться, что типы генерируются; при требовании более
      нового рантайма поднять версию `Google.Protobuf` до указанной сборкой и зафиксировать её

## 3. Проверка обязательных полей

- [x] 3.1 Добавить `src/LtiIntegrationGateway/Contracts/MessageContractValidator.cs`: проверка
      `OutboundRequest` на непустые `message_id`, `product_id`, `channel_name`, `relative_path`,
      `http_method` и на обязательность `content_type` при непустом `body`; результат — список
      найденных нарушений с именами полей (без исключений на потоке управления)
- [x] 3.2 Покрыть тестами: полный запрос проходит; отсутствие `product_id` даёт нарушение;
      тело без `content_type` даёт нарушение

## 4. Тесты контракта и профиля

- [x] 4.1 Round-trip `OutboundRequest`: `body` сохраняется байт в байт, `query_parameters`
      сохраняют порядок и повторы
- [x] 4.2 Round-trip `OutboundResult`: `outcome`, `http_status_code`, `response_body`,
      `error_code`, `attempt_count`
- [x] 4.3 Round-trip `InboundMessage`: `received_at` как `Timestamp`, `query_parameters`
      сохраняют повторы, полей `path_parameters` нет
- [x] 4.4 Неизвестное поле в JSON не ломает разбор и не заполняет известные поля мусором
- [x] 4.5 Имена на проводе в camelCase; разбор принимает и snake_case, и camelCase
- [x] 4.6 Отсутствующее `optional`-поле не появляется в JSON и отличимо от пустой строки
- [x] 4.7 Тест по дескрипторам: среди полей сообщений окружения нет `google.protobuf.Any`

## 5. Проверка и документация

- [x] 5.1 Выполнить `dotnet build LtiIntegrationGateway.slnx` — генерация типов без ошибок
- [x] 5.2 Выполнить `dotnet test LtiIntegrationGateway.slnx` — все тесты проходят
- [x] 5.3 Добавить в `README.md` короткий раздел о контрактах: где лежит `.proto`, как
      сгенерировать типы (`dotnet build`), что профиль кодирования обязателен и описан в шапке
- [x] 5.4 В итоговом отчёте указать фактически выполненные команды и результаты; отдельно
      отметить, что сериализатор Rebus, заголовки `rbs2-*` и управляющие контракты не реализованы
