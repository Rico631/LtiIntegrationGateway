# Design

## Context

Сейчас в приложении есть только обычное логирование и `/health/live`; `AddHealthChecks()` уже
зарегистрирован, но readiness-проверок нет. Rebus работает на in-memory-транспорте, исходящие
результаты доставляет `OutboundResultDispatcher`, а входящий и исходящий контуры имеют собственные
журналы. ADR §17 задаёт состав атрибутов и метрик, но конкретные SDK, экспортёр и readiness-проверки
не выбраны (ADR §20). См. `proposal.md` — Why.

## Goals / Non-Goals

**Goals:**

- Подключить OpenTelemetry traces и metrics с OTLP-экспортом, выключенным по умолчанию.
- Дать единый `ActivitySource`/`Meter` для бизнес-операций и использовать стандартное
  инструментирование ASP.NET Core, `HttpClient`, Rebus и .NET runtime.
- Добавить readiness-пробу хранилища, не меняя liveness.
- Проверить тестами отсутствие секретов и тел в телеметрии.

**Non-Goals:**

- OTel logs, collector, дашборды и алерты.
- Метрики длины очередей промышленного транспорта: на in-memory-транспорте такой величины нет;
  измеряется только backlog доставки результатов в БД.
- Изменение контрактов `.proto`, `InboundMessage`, `OutboundResult` и публичного API.
- DistributedLock, промышленный транспорт/БД, OAuth2 и JWT (ADR §20).

## Decisions

### 1. Пакеты и точка подключения

Добавить в `LtiIntegrationGateway.csproj`:

- `OpenTelemetry.Extensions.Hosting` 1.20.1;
- `OpenTelemetry.Instrumentation.AspNetCore` 1.20.1;
- `OpenTelemetry.Instrumentation.Http` 1.20.1;
- `OpenTelemetry.Instrumentation.Runtime` 1.20.1;
- `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.20.1;
- `Rebus.OpenTelemetry` 1.4.0 (совместим с Rebus 8; подтягивает `Rebus.Diagnostics`).

Регистрация — `Infrastructure/Observability/ObservabilityExtensions.AddGatewayObservability`.
Она вызывается из `Program.cs` до `builder.Build()`. Отдельный project/abstraction не создаётся:
это инфраструктурный слой приложения (ADR §4.2).

### 2. Конфигурация

`OtelOptions` читается из секции `Observability:Otel`:

| Ключ | По умолчанию | Правило |
|---|---|---|
| `Enabled` | `false` | при `false` OTel provider и экспортёры не создаются |
| `Endpoint` | — | обязателен при `Enabled=true`; абсолютный `http`/`https` URI |
| `ServiceName` | `LtiIntegrationGateway` | непустая строка |
| `ServiceVersion` | версия сборки | информационная версия |
| `SamplingRatio` | `1.0` | от `0` до `1` включительно |
| `ExportIntervalSeconds` | `60` | от `1` до `3600` |

Некорректное значение останавливает запуск с сообщением, содержащим имя ключа, но не значение
секрета. OTLP-протокол — gRPC; авторизация коллектора и дополнительные headers в этот change не
входят. При выключенной телеметрии `AddOpenTelemetry` не вызывается, поэтому экспортёры не
создаются и сетевых вызовов нет.

### 3. Ресурс и провайдеры

Ресурс: `service.name`, `service.version`, `deployment.environment` из окружения и
`service.instance.id` (GUID экземпляра). Трассировка использует `ParentBasedTraceIdRatioBasedSampler`
с настроенным коэффициентом. Metrics — `PeriodicExportingMetricReader` с настроенным интервалом.
OTLP endpoint передаётся явно из `OtelOptions`.

Tracing builder:

- `AddSource(GatewayTelemetry.ActivitySourceName)`;
- `AddAspNetCoreInstrumentation` и `AddHttpClientInstrumentation` с отключённой записью исключений;
- `AddRebusInstrumentation`;
- `AddOtlpExporter`.

Metrics builder:

- `AddMeter(GatewayTelemetry.MeterName)`;
- `AddAspNetCoreInstrumentation`, `AddHttpClientInstrumentation`, `AddRuntimeInstrumentation`;
- `AddRebusInstrumentation`;
- `AddOtlpExporter` с тем же endpoint и интервалом;
- `AddView` для стабильных границ гистограмм длительности (5, 10, 25, 50, 100, 250, 500,
  1000, 2500, 5000, 10000 мс) и числа попыток (1, 2, 3, 4, 5).

Rebus включается в `RebusConfigurationExtensions` через `.Options(o => o.EnableDiagnosticSources())`;
это добавляет spans обработки сообщений и публикации. Пакет `Rebus.OpenTelemetry` используется и
для traces, и для metrics (у него есть `AddRebusInstrumentation` для обоих builder'ов).

### 4. Единые имена и бизнес-атрибуты

`Infrastructure/Observability/GatewayTelemetry` содержит:

- `ActivitySourceName = "LtiIntegrationGateway"`;
- `MeterName = "LtiIntegrationGateway"`;
- helper для старта activity и безопасной установки тегов.

Атрибуты span'ов (только при наличии значения, без пустых строк):

- `message_id`, `correlation_id`, `product_id`, `channel`;
- `direction` (`inbound`/`outbound`), `http.request.method`, `http.route`, `url.path`;
- `operation_id`, `outcome`, `error_code`, `http.response.status_code`, `attempt_count`,
  `channel.revision` (номер активной ревизии, если известен).

`message_id`/`correlation_id` используются в traces, но не в metrics: они неограниченно
кардинальны. Тела, `Authorization`, `Cookie`, значения секретов и полные тексты исключений не
устанавливаются. Для ошибок используется безопасный код, а не `ErrorMessage`.

### 5. Точки инструментирования

- **Inbound**: `InboundRequestHandler` создаёт внутренний span `inbound.request` вокруг обработки
  после выбора маршрута; теги — канал, метод, шаблон, `operation_id`, HTTP-статус, `error_code`.
  В `finally` записывается длительность и счётчик запроса.
- **Outbound**: `ProcessOutboundRequestHandler` создаёт span `outbound.operation` вокруг сценария;
  теги — `message_id`, `product_id`, канал, исход, `error_code`, число попыток. HTTP-вызов остаётся
  дочерним span'ом стандартного `HttpClient`-инструментирования.
- **Доставка**: `OutboundResultDispatcher` создаёт span `outbound.result.delivery` на каждую
  попытку отправки сохранённого результата; теги — `message_id`, канал, успех/ошибка.
- **Rebus**: стандартные spans пакета `Rebus.OpenTelemetry`; собственные spans вкладываются в них.
- **ASP.NET Core и HttpClient**: стандартные метрики/спаны, без ручного дублирования.

### 6. Бизнес-метрики

`Infrastructure/Observability/GatewayMetrics` — singleton, создаёт инструменты из `Meter`:

- `lti.gateway.inbound.requests` — counter, теги `channel`, `http.request.method`, `http.route`,
  `http.response.status_code`, `error_code`;
- `lti.gateway.inbound.duration` — histogram, миллисекунды, те же измерения;
- `lti.gateway.outbound.operations` — counter, теги `product_id`, `channel`, `outcome`,
  `error_code`;
- `lti.gateway.outbound.duration` — histogram, миллисекунды;
- `lti.gateway.outbound.attempts` — histogram числа попыток;
- `lti.gateway.outbound.pending` — observable gauge без измерений.

Gauge обновляет отдельный `OutboundBacklogMetricsUpdater` (`BackgroundService`) с интервалом
15 секунд: он считает `Pending`-записи с непустым адресом и существующей операцией. Запрос
переиспользует общий предикат с `OutboundResultDispatcher`, чтобы метрика и выборка не расходились.
При ошибке БД обновление пропускается с warning; readiness отвечает за доступность хранилища.
Счётчики и гистограммы вызываются из обработчиков; при выключенной телеметрии они не имеют
слушателей и не создают экспортёров. Обновлятор gauge регистрируется только при
`Observability:Otel:Enabled=true`, поэтому выключенный режим не создаёт фоновых запросов к БД.

### 7. Readiness

`Infrastructure/Health/DatabaseHealthCheck` получает `IServiceScopeFactory`, создаёт scope и
вызывает `GatewayDbContext.Database.CanConnectAsync` с таймаутом 5 секунд. Возвращает `Healthy` или
`Unhealthy` с общим описанием без строки подключения. Регистрация:

```text
AddSingleton<DatabaseHealthCheck>()
AddHealthChecks().AddCheck<DatabaseHealthCheck>("storage", tags: ["ready"])
MapHealthChecks("/health/live", predicate: _ => false)
MapHealthChecks("/health/ready", predicate: check => check.Tags.Contains("ready"))
```

Проверка Rebus в readiness не добавляется: на in-memory-транспорте нет внешней зависимости, а
промышленный transport появится отдельным change. Это ограничение фиксируется в README.

### 8. Тесты

- `ActivityListener` и `MeterListener` — проверка пользовательских spans/метрик без коллектора.
- `DatabaseHealthCheck` — успешная проверка SQLite in-memory и отказ при недоступной БД;
  `GET /health/ready` — `200`/`503` на тестовом хосте.
- Конфигурация: disabled не создаёт provider; enabled с некорректным endpoint/ratio/interval
  останавливает запуск; enabled с корректными значениями регистрирует providers.
- Безопасность: запрос с маркерным секретом и телом не оставляет маркер в тегах activity и
  измерениях метрик.
- Регрессия liveness: `/health/live` остаётся `200` без обращений к БД.

## Risks / Trade-offs

- **Шум и стоимость метрик runtime** → включение только через `Enabled`, настраиваемый интервал
  экспорта; при необходимости runtime-метрики можно отключить отдельно.
- **Кардинальность метрик** → в измерениях только канал, продукт, исход, код ошибки и маршрут;
  `message_id`/`correlation_id` остаются только в traces.
- **Gauge pending создаёт периодический запрос к БД** → интервал 15 секунд, индекс
  `(DeliveryStatus, CreatedAt)` уже существует; ошибки не ломают приложение.
- **Readiness видит только БД** → на текущем in-memory-транспорте это единственная внешняя
  зависимость; при промышленном транспорте проверка будет расширена отдельным change.
- **Rebus.OpenTelemetry может фиксировать exception events** → в тестах проверить состав
  атрибутов; при наличии текста исключения отключить запись exception или добавить безопасный
  фильтр, не нарушая требование о секретах.
- **OTLP endpoint без коллектора** → при включении без доступного коллектора экспортёр может
  писать диагностические ошибки, но не должен останавливать обработку запросов; это свойство
  OTel SDK, а не бизнес-ошибка.

## Migration Plan

1. Выпустить версию с `Enabled=false` — поведение и зависимости не меняются.
2. Включить `Observability:Otel:Enabled` и задать endpoint в среде с коллектором.
3. При проблемах выключить флаг и перезапустить приложение; откат не требует миграции БД.
4. Readiness можно включить сразу: при отсутствии хранилища она возвращает `503`, liveness
   остаётся `200`.

## Open Questions

- Нужен ли отдельный OTel logs pipeline взамен текущего логирования — вне объёма.
- Нужна ли сквозная корреляция входящих вызовов через `correlation_id` — отдельное решение,
  требующее источника идентификатора.
- Какие дашборды и алерты нужны эксплуатации — определяются после появления коллектора.


