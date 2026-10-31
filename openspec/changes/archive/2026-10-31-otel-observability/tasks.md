# Tasks

## 1. Конфигурация и зависимости

- [x] 1.1 Добавить в `LtiIntegrationGateway.csproj` пакеты OpenTelemetry 1.20.1 (hosting,
      ASP.NET Core, HttpClient, runtime, OTLP exporter) и `Rebus.OpenTelemetry` 1.4.0; проверить
      `dotnet build LtiIntegrationGateway.slnx` без предупреждений.
- [x] 1.2 Реализовать `OtelOptions` и загрузку секции `Observability:Otel` с валидацией endpoint,
      `SamplingRatio` и `ExportIntervalSeconds`; добавить TUnit-тесты на значения по умолчанию,
      выключенный режим и каждый некорректный параметр; проверить, что сообщение об ошибке содержит
      только имя ключа.
- [x] 1.3 Описать ключи конфигурации в README и проверить документированные значения по умолчанию
      против `OtelOptions`.

## 2. Провайдеры и примитивы телеметрии

- [x] 2.1 Добавить `GatewayTelemetry` (`ActivitySource`/`Meter`, безопасные хелперы тегов) и
      `GatewayMetrics` со счётчиками, гистограммами и gauge; покрыть имена инструментов и
      измерения тестами `ActivityListener`/`MeterListener`.
- [x] 2.2 Реализовать `AddGatewayObservability`: OTLP-экспорт traces/metrics, resource
      (`service.name`, `service.version`, окружение), sampler, interval, views гистограмм;
      проверить тестами, что при `Enabled=false` провайдеры не создаются, а при корректном
      `Enabled=true` регистрируются `TracerProvider` и `MeterProvider`.
- [x] 2.3 Включить диагностические источники Rebus и `AddRebusInstrumentation`; добавить
      интеграционный тест с in-memory-шиной и `ActivityListener`, подтверждающий span обработки
      сообщения.
- [x] 2.4 Обновить `ARCHITECTURE.md` описанием `Infrastructure/Observability` и связью с
      `Program.cs`/Rebus; проверить, что перечисленные компоненты существуют в коде.

## 3. Входящий контур

- [x] 3.1 Добавить span `inbound.request` и метрики `lti.gateway.inbound.requests`/
      `lti.gateway.inbound.duration` в `InboundRequestHandler`; покрыть успешный и отклонённый
      вызовы тестами с `ActivityListener` и `MeterListener`.
- [x] 3.2 Проверить, что теги входящего span содержат канал, метод, шаблон, `operation_id`,
      HTTP-статус и безопасный `error_code`, но не тело и не query-значения.

## 4. Исходящий контур

- [x] 4.1 Добавить span `outbound.operation` и метрики `lti.gateway.outbound.operations`/
      `lti.gateway.outbound.duration`/`lti.gateway.outbound.attempts` в
      `ProcessOutboundRequestHandler`; покрыть успех, транспортную ошибку и отказ тестами.
- [x] 4.2 Добавить span `outbound.result.delivery` и счётчик доставки в
      `OutboundResultDispatcher`; проверить успешную и неуспешную отправку через `OutboundFixture`.
- [x] 4.3 Вынести общий предикат выборки `Pending`-записей и добавить
      `OutboundBacklogMetricsUpdater`, обновляющий gauge `lti.gateway.outbound.pending`; тестами
      проверить количество, отсутствие записей без адреса/операции и устойчивость к ошибке БД.

## 5. Readiness и защита телеметрии

- [x] 5.1 Реализовать `DatabaseHealthCheck` с таймаутом и scope для `GatewayDbContext`;
      зарегистрировать `/health/ready` с тегом `ready`, сохранить `/health/live` без зависимостей.
- [x] 5.2 Добавить тесты health check: доступная SQLite in-memory — `Healthy`; недоступная БД —
      `Unhealthy`; `GET /health/ready` — `200`/`503`; `GET /health/live` остаётся `200` при отказе
      хранилища.
- [x] 5.3 Добавить тест безопасности: маркерные секрет и тело запроса не появляются в тегах
      activity и измерениях метрик; проверить отсутствие `Authorization`/`Cookie`.
- [x] 5.4 Обновить README (health endpoints, конфигурация, ограничения readiness) и ADR §17/§20;
      проверить, что документы не обещают OTel logs или метрики транспорта.

## 6. Интеграционная проверка

- [x] 6.1 Запустить `dotnet build LtiIntegrationGateway.slnx` и полный `dotnet test`; зафиксировать
      реальные результаты и отсутствие предупреждений.
- [x] 6.2 Запустить `openspec validate 2026-10-31-otel-observability --strict` и проверку
      документации `.proto` по `AGENTS.md`; прото-контракты в этом change не меняются.
