# Design

## Context

См. `proposal.md` — Why. Существенное для реализации:

- Шлюз — минимальный ASP.NET Core хост (`Program.cs`): `AddHealthChecks`, `AddGatewayRebus`,
  EF Core, единственный статический endpoint `/health/live`. Динамических маршрутов нет.
- ADR §5.2 фиксирует выбранный механизм — кастомный `EndpointDataSource` с `RouteEndpointBuilder`
  и метаданными, перестраиваемый при активации ревизии, и зафиксированный fallback — собственный
  матчер в middleware поверх `Map("/{**path}")`.
- Тестовая инфраструктура: `GatewayWebApplicationFactory : WebApplicationFactory<Program>`
  переопределяет `ConnectionStrings:Gateway` на SQLite in-memory и включает `Testing`; тесты TUnit.
- Контракт `InboundMessage` уже объявлен в `.proto`, но в этом изменении не используется:
  публикация в Rebus — следующий change.

## Goals / Non-Goals

Цели дизайна:

- минимальный производственный след, который можно оставить в коде после подтверждения механизма;
- проверка всех пяти критериев ADR §5.2 в одном интеграционном тестовом наборе;
- сохранение совместимости со статическими endpoint'ами и шиной (ничего из существующего не
  переключается на новую механику).

Вне дизайна:

- персистентность наборов маршрутов и ревизий контрактов (EF Core), загрузка OpenAPI, валидация
  тела, публикация `InboundMessage`, авторизация входящих вызовов;
- реализация fallback-матчера: он остаётся зафиксированным вариантом на случай провала критериев.

## Decisions

### D1. Каталог маршрутов — in-memory singleton с неизменяемыми снапшотами

```text
InboundRouteDefinition   (ChannelName, HttpMethod, Template)
InboundRouteSet          (RevisionKey, IReadOnlyList<InboundRouteDefinition>)
InboundRouteCatalog      Activate(set) -> problems; Deactivate(revisionKey); Snapshot()
InboundRouteSetValidator Validate(set, activeSets) -> problems
```

Каталог хранит активные наборы в словаре по `RevisionKey`, а наружу отдаёт неизменяемый массив
маршрутов, собранный при изменении. Изменение состояния и смена снапшота выполняются под одной
блокировкой, чтение снапшота — без блокировки.

**Почему in-memory:** спайк проверяет механику ASP.NET Core, а не хранение. Ревизии контрактов в
БД появятся в change про OpenAPI и inbound-контур; здесь каталог — точка подключения будущего
хранилища.

**Альтернативы:** EF Core сразу (лишняя миграция и объём без влияния на критерии); статическая
регистрация в `Program.cs` (не проверяет рантайм-активацию).

### D2. `InboundEndpointDataSource : EndpointDataSource`

```text
Endpoints          закэшированный массив RouteEndpoint, построенный из снапшота каталога
GetChangeToken()   CancellationToken текущего поколения
```

При изменении каталога источник пересоздаёт `CancellationTokenSource` и отменяет предыдущий токен;
маршрутизация ASP.NET Core перестраивает matcher по этому токену. Источник создаётся в DI и
добавляется в набор источников приложения:

```csharp
services.AddSingleton<InboundEndpointDataSource>();
var endpointRouteBuilder = (IEndpointRouteBuilder)app;
endpointRouteBuilder.DataSources.Add(app.Services.GetRequiredService<InboundEndpointDataSource>());
```

**Уточнение по результатам спайка:** регистрация `EndpointDataSource` только в DI
(`services.AddSingleton<EndpointDataSource>(...)`) в minimal hosting не подхватывается — маршруты
остаются необслуживаемыми (`404`). Рабочий вариант — добавление источника в
`IEndpointRouteBuilder.DataSources`, что и зафиксировано в ADR §5.2.

**Альтернатива:** `Map("/{**path}")` + собственный матчер (fallback ADR) — отложен, потому что
теряет штатные возможности endpoint'ов.

### D3. Построение endpoint'а и метаданные

Каждый маршрут превращается в `RouteEndpoint`:

```text
RouteEndpointBuilder(requestDelegate, RoutePatternFactory.Parse(template), order: 0)
    .Metadata.Add(HttpMethodMetadata([method]))
    .Metadata.Add(new InboundChannelMetadata(channelName))
    .Metadata.Add(new EnableRateLimitingAttribute(RateLimitPolicyName))
```

Приоритет между пересекающимися шаблонами не задаётся вручную: используется штатное сравнение
`RoutePattern` (литерал выше параметра). `order` одинаков (0) для всех динамических маршрутов.

**Почему без ручного `Order`:** ручные приоритеты пришлось бы сопровождать и объяснять; штатное
сравнение уже детерминировано и покрыто тестом критерия 3.

### D4. Валидация набора до активации

`InboundRouteSetValidator` проверяет:

1. метод входит в поддерживаемый набор (`GET`, `POST`, `PUT`, `PATCH`, `DELETE`);
2. шаблон разбирается `RoutePatternFactory.Parse` (ошибка разбора — проблема);
3. нет дубликатов «метод + нормализованный шаблон» внутри набора;
4. нет конфликта «метод + шаблон» с уже активными наборами.

При наличии проблем `Activate` не меняет состояние и возвращает список проблем. Это реализует
ADR §5.2 «конфликт обнаруживается при публикации, не в рантайме» в масштабе спайка: в рантайм
не попадает набор, который мог бы дать неоднозначный выбор.

**Альтернатива:** разрешать конфликт в рантайме по порядку регистрации — отклонено: результат
зависел бы от порядка активации.

### D5. Ограничение частоты на маршрут

`AddRateLimiter` с единственной политикой `inbound-per-channel`:

```text
RateLimitPartition.GetFixedWindowLimiter(
    partitionKey: InboundChannelMetadata канала из endpoint'а,
    options: PermitLimit = InboundRateLimitOptions.PermitLimit,
             Window     = TimeSpan.FromSeconds(InboundRateLimitOptions.WindowSeconds))
```

Лимиты задаются конфигурацией (`Inbound:RateLimit:PermitLimit`, `Inbound:RateLimit:WindowSeconds`),
`app.UseRateLimiter()` ставится после маршрутизации. Ключ партиции — канал, а не путь: ADR §10.2
требует квоту на канал, и это делает квоты каналов независимыми.

**Почему fixed window:** поведение детерминированно и проверяется тестом без ожидания скользящего
окна; конкретный алгоритм ADR не фиксирует, решение можно заменить без изменения спек.

### D6. Probe-обработчик динамического маршрута

`InboundProbeHandler` (singleton, регистрируется в DI) возвращает `200` и JSON:

```json
{ "httpMethod": "GET", "routeTemplate": "/callbacks/{name}", "relativePath": "/callbacks/payments", "channel": "bank-a" }
```

`routeTemplate` берётся из `RouteEndpoint.RoutePattern.RawText`, `relativePath` — из
`HttpContext.Request.Path` (без query string, без percent-декодирования). Зависимостей от `IBus`
и `GatewayDbContext` у обработчика нет: публикация `InboundMessage` появится в inbound-change.

**Почему отдельный класс:** probe — временный контракт спайка; когда появится `202`-ответ,
меняется только `RequestDelegate`, а источник endpoint'ов и каталог остаются.

### D7. Тесты

`WebApplicationFactory<Program>` + резолв `InboundRouteCatalog` из DI теста:

- активация и деактивация набора — HTTP-запросы через `factory.CreateClient()`;
- конфликты — через возвращённые проблемы и последующие HTTP-запросы;
- приоритет шаблонов — по полю `routeTemplate` в ответе;
- ограничение частоты — лимит 2 запроса на окно 60 с для канала `bank-a`, независимость — запрос
  по каналу `bank-b`; восстановление — окно 1 с и ожидание чуть больше секунды;
- статические endpoint'ы — `/health/live` = `200` при активных динамических маршрутах.

**Почему тест на реальном хосте:** критерий ADR — «поведение подтверждено интеграционным тестом»,
а не проверка классов по отдельности.

### D8. Фиксация результата

После зелёных тестов ADR §5.2 дополняется фактическим результатом: версия .NET, что подтверждено
(активация, деактивация, конфликты, приоритеты, лимиты), и вывод — выбранный механизм подтверждён
или требуется fallback. README получает короткий раздел о динамических маршрутах.

## Risks / Trade-offs

- **[Перестройка matcher под нагрузкой]** → в спайке проверяется корректность, не производительность;
  стоимость активации фиксируется как открытый вопрос для inbound-change.
- **[Кэш endpoint'ов может не обновиться, если маршрутизация запросила `Endpoints` до первого
  изменения]** → тест критерия 1 делает HTTP-запрос до активации (`404`) и после (`200`).
- **[Ограничитель частоты и тестовое время]** → лимиты и окно задаются конфигурацией теста;
  сценарий восстановления использует окно 1 с, чтобы избежать длинных тестов.
- **[Конфигурация каналов и маршруты спайка не связаны]** → в спайке маршрут объявляет канал сам;
  связь с `ChannelRevision` появится при загрузке OpenAPI, где шаблоны приходят из документа.
- **[Probe-ответ может быть принят за контракт]** → в README и ADR фиксируется, что ответ
  диагностический и будет заменён `202`/`503` в inbound-change.
- **[Порядок `UseRateLimiter` и маршрутизации]** → тест критерия 4 падает, если middleware стоит
  не на месте; порядок проверяется именно этим тестом.

## Migration Plan

1. Код аддитивен: новые файлы + несколько регистраций в `Program.cs`; база данных и контракты
   Protobuf не меняются.
2. Проверка: `dotnet build`, `dotnet test`, ручной запуск и проверка `/health/live`.
3. Откат: revert изменения целиком; отдельный флаг не нужен, так как каталог пуст до первой
   активации и динамических маршрутов в эксплуатации не появляется.

## Open Questions

- Источник наборов маршрутов (ревизии контрактов в БД) и их жизненный цикл — следующий change.
- Нужен ли отдельный `Order` для маршрутов разных каналов, если шаблоны полностью совпадают, —
  сейчас такой случай запрещён валидацией.
- Алгоритм ограничения частоты для production (fixed window выбран из-за детерминированности).

