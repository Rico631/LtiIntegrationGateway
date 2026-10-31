# Design

## Контекст

ADR §15.2 требует endpoint состояния канала (наличие ключа секрета, срок действия сертификата,
последние ошибки), §13 — отражать срок действия клиентского сертификата. Существующие срезы читают
канал по имени (`Features/Channels/GetChannel`), но не проверяют секреты и не агрегируют ошибки.

## Контракт

`lti.gateway.management.v1`, аддитивное расширение:

```text
rpc GetChannelHealth (GetChannelHealthRequest) returns (GetChannelHealthResponse)
    GET /v1/channels/{channel_name}/health

message GetChannelHealthRequest {
  string channel_name = 1;
}

message GetChannelHealthResponse {
  string channel_name = 1;
  string state = 2;                    // ACTIVE | NO_ACTIVE_REVISION
  int32 active_revision = 3;           // 0 — активной ревизии нет
  bool secret_configured = 4;
  string secret_config_key = 5;        // имя ключа, не значение
  google.protobuf.Timestamp certificate_expires_at = 6;
  repeated ChannelHealthError errors = 7;
  google.protobuf.Timestamp checked_at = 8;
}

message ChannelHealthError {
  google.protobuf.Timestamp occurred_at = 1;
  string direction = 2;                // outbound | inbound
  string error_code = 3;
  int32 http_status = 4;               // 0 — статуса нет
  string outcome = 5;                  // исход для исходящих, пусто для входящих
}
```

Все поля документируются по `AGENTS.md` (комментарий перед каждым сообщением и полем, значение `0`
и пустой строки описываются явно).

## Срез

```text
Features/Channels/GetChannelHealth
  GetChannelHealthQuery(channelName)
  ChannelHealthResult(State, ActiveRevision, SecretConfigured, SecretConfigKey,
                      CertificateExpiresAt, Errors, CheckedAt)
  GetChannelHealthHandler(GatewayDbContext, ISecretStore, IChannelCertificateStore,
                          ManagementAuditWriter)
```

Порядок:

1. Канал по имени с активной ревизией; нет канала — `null` (фасад отвечает `NotFound`).
2. Нет активной ревизии — `NO_ACTIVE_REVISION`, `secret_configured = false`, ошибки всё равно
   читаются.
3. Проверка секрета по `AuthType` активной ревизии:
   - `none` — секрет не требуется, `secret_configured = true`;
   - `bearer`/`api_key`/`basic` — `ISecretStore.Find(config_key)` непуст;
   - `client_certificate` — `IChannelCertificateStore.GetInfo(config_key)`: `secret_configured =
     Found && Valid`, `certificate_expires_at` возвращается и для истёкшего/не вступившего в силу
     сертификата;
   - пустой `config_key` при типе, требующем секрет, — `false`.
4. Последние ошибки: по 5 записей из `OutboundMessageLog` и `InboundMessageLog` для канала с
   `ErrorCode != null`; выборка выполняется в SQL с `ORDER BY CreatedAtUnixMs DESC` /
   `ReceivedAtUnixMs DESC` и `LIMIT` (индексы `(ChannelName, CreatedAtUnixMs)` и
   `(ChannelName, ReceivedAtUnixMs)`), поэтому в окно попадают именно последние записи. После
   выборки направления объединяются и сортируются по времени убыванию. Тела и заголовки не читаются
   в ответ.
5. `CheckedAt = TimeProvider.GetUtcNow()`; аудит `GetChannelHealth` (успех/отказ).

## Ошибки

- неизвестный канал — gRPC `NotFound` / REST `404`;
- внутренняя ошибка — gRPC `Internal` / REST `500`, детали только в лог;
- отсутствие секрета не является ошибкой вызова: это диагностическое состояние в ответе.

## Безопасность

- значение секрета не запрашивается в ответ и не логируется;
- возвращается только имя ключа конфигурации;
- тела сообщений не попадают в ответ, хотя хранятся в журналах (ADR §13);
- endpoint закрыт ключом управляющего API, как остальные операции.

## Тестируемость

- unit-тест среза: канал с активной ревизией и секретом; канал без активной ревизии; секрет не
  настроен; истёкший и ещё не вступивший в силу сертификат (срок возвращается, `secret_configured =
  false`);
- интеграционные тесты REST и gRPC: `404` для неизвестного канала, паритет ответов, отсутствие
  значения секрета, последние ошибки из обоих журналов, запись аудита, `401`/`503` без ключа API;
- тест ограничения: не более пяти ошибок на направление;
- тест окна: более 64 ошибок по каналу — возвращаются именно последние (порядок задаётся в SQL по
  `CreatedAtUnixMs`/`ReceivedAtUnixMs`).

## Миграции

- `AddInboundReceivedAtUnixMs` — колонка и индекс для сортировки входящих ошибок;
- `AddOutboundErrorIndex` — индекс `(ChannelName, CreatedAtUnixMs)` для исходящих ошибок.

## Влияние на контракты

Существующие RPC и поля не меняются. Добавляются новый RPC и три сообщения; потребители, не
использующие операцию, не затронуты.

## Отклонения от ADR

Нет. Реализуется §15.2 и §13; наблюдаемость (метрики, OTel) остаётся открытым вопросом ADR §20.
