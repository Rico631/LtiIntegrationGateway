# Tasks

## 1. Шина и сериализация

- [x] 1.1 Добавить в `LtiIntegrationGateway.csproj` пакеты `Rebus` 8.9.5 и
      `Rebus.ServiceProvider` 10.7.2
- [x] 1.2 Добавить `src/LtiIntegrationGateway/Infrastructure/Rebus/ProtobufMessageRegistry.cs`:
      реестр из явного списка дескрипторов (`OutboundRequest`, `OutboundResult`, `InboundMessage`),
      отдаёт `TypeRegistry` для `JsonParser` и `MessageDescriptor? Find(string fullName)`
- [x] 1.3 Добавить `src/LtiIntegrationGateway/Infrastructure/Rebus/ProtobufSerializer.cs` —
      реализация `Rebus.Serialization.ISerializer`: печать через `JsonFormatter.Default` в UTF-8,
      `Headers.Type` = полное имя сообщения, `Headers.ContentType` = `application/json`, разбор
      через `JsonParser` с `IgnoreUnknownFields(true)` и реестром
- [x] 1.4 Добавить обработку ошибок в сериализаторе: объект не `IMessage` → ошибка с именем
      CLR-типа; отсутствует `rbs2-msg-type` → ошибка с именем заголовка; тип не найден в реестре →
      ошибка с полным именем типа
- [x] 1.5 Добавить `src/LtiIntegrationGateway/Infrastructure/Rebus/RebusConfigurationExtensions.cs`
      с методом `AddGatewayRebus`: in-memory транспорт на очереди `lti.gateway.outbound`,
      регистрация сериализатора через `Serialization(...)`, реестр и сериализатор как singleton
- [x] 1.6 Вызвать `builder.Services.AddGatewayRebus()` в `Program.cs`, не добавляя туда имён
      очередей и типов сообщений

## 2. Тесты сериализатора

- [x] 2.1 Заголовки и тело `OutboundRequest`: `rbs2-msg-type` равен полному имени,
      `rbs2-content-type` равен `application/json`, `body` представлен base64
- [x] 2.2 Round-trip `OutboundResult`: тип, `outcome`, статус, тело ответа, число попыток
- [x] 2.3 Round-trip `InboundMessage`: тип, фактический путь, шаблон маршрута, query-параметры,
      `received_at`
- [x] 2.4 Ошибки разбора: неизвестное имя типа даёт ошибку с этим именем; отсутствующий
      `rbs2-msg-type` даёт ошибку с именем заголовка
- [x] 2.5 Ошибка отправки: объект, не реализующий `IMessage`, даёт ошибку с именем своего типа
- [x] 2.6 Тест полноты реестра: каждое сообщение `IntegrationMessagesReflection.Descriptor`
      находится в реестре по полному имени

## 3. Интеграционный тест шины

- [x] 3.1 Тест на `Configure.With(new BuiltinHandlerActivator())` + `UseInMemoryTransport`: шина с
      тем же сериализатором доставляет сообщение обработчику, тип и значения полей совпадают;
      ожидание завершения — через `TaskCompletionSource` с таймаутом, чтобы тест не завис

## 4. Проверка и документация

- [x] 4.1 Выполнить `dotnet build LtiIntegrationGateway.slnx` — сборка без ошибок и предупреждений
- [x] 4.2 Выполнить `dotnet test LtiIntegrationGateway.slnx` — все тесты проходят
- [x] 4.3 Ручная проверка: запустить приложение и убедиться, что шина стартует без ошибок и
      приложение остаётся доступным по `/health/live`
- [x] 4.4 Добавить в `README.md` короткий раздел о шине: имя входной очереди, где лежит
      сериализатор и реестр, чем формат тела отличается от обычного JSON-сериализатора Rebus
- [x] 4.5 В итоговом отчёте указать фактически выполненные команды и результаты; отдельно отметить,
      что обработчики, маршрутизация, политика повторов и error-queue не реализованы, и что имя
      error-queue остаётся значением по умолчанию (открытый вопрос в `design.md`)
