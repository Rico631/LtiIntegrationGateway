# Design

## Context

См. `proposal.md` (Why). Состояние, влияющее на подход:

- `.proto` v1 и C#-типы уже сгенерированы, есть `Contracts/MessageContractValidator` и тесты
  профиля (`TestSupport/ContractJson.cs` — эталон настроек `JsonParser`);
- в проекте нет пакетов Rebus; транспорт по ADR §10.1 — in-memory;
- по ADR §5.3 реализуется собственный `ISerializer`, потому что `Rebus.Protobuf` работает с
  типами `protobuf-net` и несовместим с типами, сгенерированными из `.proto`;
- проверено по исходникам Rebus: `ISerializer` — два асинхронных метода; `Message(headers,
  object body)`; `TransportMessage(headers, byte[] body)`; заголовки — `Headers.Type`
  (`rbs2-msg-type`) и `Headers.ContentType` (`rbs2-content-type`); `StandardConfigurer<T>` даёт
  `Register(Func<IResolutionContext, T>)`; in-memory транспорт подключается
  `UseInMemoryTransport(InMemNetwork, inputQueueName, registerSubscriptionStorage)`; класс
  `BuiltinHandlerActivator` присутствует и пригоден для тестовой шины.

Проверенные версии: `Rebus` 8.9.5 (`net10.0`), `Rebus.ServiceProvider` 10.7.2.

## Goals / Non-Goals

**Goals:**

- рабочий обмен сообщениями через Rebus с сохранением типа и тела;
- единая точка правды для списка сообщений (реестр) и единая точка правды для формата (сериализатор);
- читаемые ошибки вместо тихой потери сообщения.

**Non-Goals:**

- обработчики, маршрутизация, очереди продуктов, error-queue политики;
- промышленный транспорт и его гарантии;
- изменение контракта или профиля кодирования.

## Decisions

### D1. Собственный `ProtobufSerializer : ISerializer`

Реализует два метода: `Serialize(Message)` и `Deserialize(TransportMessage)`.

**Почему не `Rebus.Protobuf`:** он сериализует типы `protobuf-net` (code-first с атрибутами), а у
нас типы из `.proto`; смешивать две модели нельзя, а gRPC-транскодинг в будущем требует именно
`Google.Protobuf` (ADR §5.3).

**Почему свои методы, а не наследование от стандартного JSON-сериализатора Rebus:** стандартный
требует POCO и не знает про `IMessage` и дескрипторы.

### D2. Формат тела — тот же профиль, что зафиксирован в контракте

Печать — `JsonFormatter.Default.Format(message)` в UTF-8; разбор — `JsonParser` с
`IgnoreUnknownFields(true)` и `TypeRegistry` из реестра.

**Почему настройки именно такие:** они уже зафиксированы в `ContractJson` тестов контракта и в
шапке `.proto`; сериализатор обязан вести себя так же, иначе тесты контракта и реальная шина
разойдутся. `JsonFormatter`/`JsonParser` документированы как потокобезопасные, поэтому экземпляр
сериализатора может быть singleton.

### D3. Реестр — единая точка правды о типах

`ProtobufMessageRegistry` создаётся из явного списка дескрипторов (`OutboundRequest.Descriptor`,
`OutboundResult.Descriptor`, `InboundMessage.Descriptor`, `QueryParameter.Descriptor`) и
предоставляет:

- `TypeRegistry` — для `JsonParser`;
- `MessageDescriptor? Find(string fullName)` — для восстановления типа по заголовку.

**Почему явный список, а не рефлексия по сборке:** рефлексия «найдёт» и служебные типы, и
тестовые; явный список виден в диффе, а его полноту проверяет тест, сверяющий реестр со всеми
сообщениями `IntegrationMessagesReflection.Descriptor`.

### D4. Заголовки — константы Rebus

Тип пишется в `Headers.Type`, содержимое — в `Headers.ContentType` со значением
`application/json`. Строковые литералы `rbs2-*` в коде не используются.

**Почему:** константы — часть публичного контракта Rebus; при смене имени заголовка в новой
версии компиляция покажет проблему, а литерал — нет.

### D5. Ошибки сериализации — исключение с именем типа

- объект не `IMessage` → `InvalidOperationException` с полным именем CLR-типа;
- нет заголовка типа → `InvalidOperationException` с именем отсутствующего заголовка;
- тип не найден в реестре → `InvalidOperationException` с полным именем из заголовка.

**Почему исключение, а не «пропустить сообщение»:** Rebus уводит сообщение с ошибкой в error-queue
и сохраняет контекст; молчаливый пропуск означал бы потерю данных без диагностики. Текст исключения
попадает в лог и в error-queue, поэтому обязан содержать имя типа.

### D6. Подключение шины — один метод расширения

`Infrastructure/Rebus/RebusConfigurationExtensions.cs` с `AddGatewayRebus(this IServiceCollection)`,
внутри — `services.AddRebus(...)`:

```text
Перевозчик:  UseInMemoryTransport(new InMemNetwork(), "lti.gateway.outbound")
Сериализация: Serialization(s => s.Register(_ => protobufSerializer))
Регистрация:  ProtobufMessageRegistry и ProtobufSerializer — singleton
```

`Program.cs` вызывает одну строку `builder.Services.AddGatewayRebus()` и не знает ни имён очередей,
ни типов сообщений.

**Почему отдельный метод:** состав шины (очередь, сериализатор, будущие маршруты) меняется чаще,
чем `Program.cs`; одна точка входа позволяет менять его, не трогая composition root.

**Известное расхождение, которое фиксируется, а не замалчивается:** ADR §10.2 называет error-queue
`lti.gateway.error`, а Rebus по умолчанию использует `<входная очередь>.error`, то есть
`lti.gateway.outbound.error`. Публичный сеттер имени error-queue в `Options` текущей версии не
обнаружен; настройка error-handling в этот change не входит. Поэтому имя error-queue остаётся
значением по умолчанию, и это выносится в открытые вопросы, а не выдаётся за выполненное требование.

### D7. Интеграционный тест поднимает собственную шину

Тест использует классический API Rebus: `Configure.With(new BuiltinHandlerActivator())`,
`UseInMemoryTransport(new InMemNetwork(), "test.input")`, тот же экземпляр сериализатора, и
обработчик, складывающий полученное сообщение в `TaskCompletionSource`.

**Почему не `WebApplicationFactory` и не `IHost`:** проверяется поведение сериализации в шине, а не
хост приложения; собственный in-memory bus-инстанс даёт детерминированный тест без гонок с
фоновым хостом. Проверка «приложение стартует с шиной» остаётся в существующем наборе тестов хоста.

## Risks / Trade-offs

- **[Реестр и контракт разъезжаются]** → тест сверяет реестр со всеми сообщениями окружения `v1`;
  добавление сообщения без регистрации ломает тест, а не прод.
- **[Формат тела жёстко ProtoJSON]** → переход на бинарный protobuf потребует правки только
  сериализатора; `.proto` и контракт не меняются.
- **[Имя error-queue не совпадает с ADR]** → зафиксировано выше как открытый вопрос; проверяется
  при настройке error-handling.
- **[Версии Rebus и Rebus.ServiceProvider]** → обе фиксируются; несовместимость проявится на
  сборке, а не в рантайме.
- **[Ошибки сериализации видны только в логах]** → текст исключения содержит имя типа и категорию,
  поэтому диагностика не требует разбора бинарного тела.

## Migration Plan

1. Миграция данных не требуется: контракт и профиль не меняются, шина подключается впервые.
2. Проверка: `dotnet build`, `dotnet test`, затем запуск приложения и проверка, что шина стартует
   без ошибок.
3. Откат: revert коммита; очередь in-memory исчезает вместе с процессом.

## Open Questions

- Имя error-queue: привести к `lti.gateway.error` через настройку error-handling Rebus или изменить
  ADR §10.2 — решается в change про обработку ошибок и повторы шины.
