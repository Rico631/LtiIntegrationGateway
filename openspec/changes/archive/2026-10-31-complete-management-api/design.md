# Design

## Контекст

Все 18 RPC `ManagementService` реализованы в `Features/Management`. Незавершённость — в побочных
эффектах и документации:

| Место | Сейчас | Требование ADR §15.1/§15.2 |
|---|---|---|
| `GetChannel` | аудит не пишется | чтение фиксируется |
| `UpdateChannelDraft` | неизвестный канал → `NotFound` без аудита | отказ фиксируется |
| `PublishChannelRevision` | то же | отказ фиксируется |
| `ActivateChannelRevision` | то же | отказ фиксируется |
| `RollbackChannelRevision` | то же | отказ фиксируется |
| `UpdateContractVersionSettings` | отказ валидации до среза без аудита | отказ фиксируется |
| `GetChannelHealth` | аудит в срезе | уже соответствует |
| `UploadContract`, `Publish/ActivateContractVersion` | аудит в срезах | уже соответствует |

## Аудит в фасаде

`RequireChannelIdAsync` получает имя операции:

```text
RequireChannelIdAsync(operation, channelName, ct):
    id = Channels.Where(name).Select(Id).SingleOrDefault()
    if id is null:
        AuditAsync(operation, channelName, false, ["channel: канал не найден"])
        throw RpcException(NotFound)
    return id
```

Вызовы:

| Метод | Операция в аудите | Дополнительный аудит успеха |
|---|---|---|
| `GetChannel` | `GetChannel` | да, после чтения |
| `UpdateChannelDraft` | `UpdateChannelDraft` | уже есть |
| `PublishChannelRevision` | `PublishChannelRevision` | уже есть |
| `ActivateChannelRevision` | `ActivateChannelRevision` | уже есть |
| `RollbackChannelRevision` | `RollbackChannelRevision` | уже есть |

Срезы этих операций при неизвестном канале не вызываются, поэтому дублей аудита нет. Ветки
`channel: канал не найден` в срезах остаются для прямых вызовов из кода и тестов.

## Отказы валидации настроек версии

`UpdateContractVersionSettings` до вызова среза проверяет наличие настроек и тип авторизации. При
проблемах:

```text
AuditAsync("UpdateContractVersionSettings", $"{channel}:v{version}", false, problems)
return ContractOperationResponse(succeeded = false, problems)
```

Целевой объект совпадает с целевым объектом среза (`UpdateContractSettings`), но имя операции в
аудите — имя RPC, чтобы записи фасада были единообразны. Срез при проблемах валидации аудит не пишет;
дублей нет.

## Контракт и документация

- `ChannelSettings.auth_type`: убрать утверждение, что `client_certificate` не поддерживается;
  перечислить поддерживаемые типы и правило «значение секрета не возвращается».
- `OperationProblem.code`: дополнить фактическими кодами `queue`, `address`, `rate_limit` (сейчас
  перечень неполон).
- README: убрать «управляющий API маршрутов продуктов не реализован», описать аудит чтений и
  `NotFound` для неизвестного канала.

Поля, номера и состав RPC не меняются — изменения только в комментариях и документации.

## Тестируемость

- интеграционный тест `GetChannel`: успешное чтение оставляет запись `GetChannel` с результатом
  `succeeded`;
- табличный тест для четырёх изменяющих операций: неизвестный канал → `404` и запись аудита
  `rejected`;
- тест `GetChannel` по неизвестному каналу: `404` + запись аудита;
- тест `UpdateContractVersionSettings` без настроек и с неизвестным `auth_type`: отказ и запись
  аудита;
- проверка, что `GetChannelHealth` по-прежнему оставляет ровно одну запись аудита (аудит среза, без
  дубля из фасада).

## Влияние на контракты

Protobuf-поля и RPC не меняются; правятся только комментарии. Поведение API дополняется аудитом и
`NotFound`, что не ломает существующих потребителей.

## Отклонения от ADR

Нет. Реализуется §15.1 и §15.2; состав логического перечня методов не расширяется.
