# Design

## Контекст

Изменение закрывает решения ADR §7.5 (типы авторизации исходящего канала) и §7.7 (жизненный цикл
HTTP-соединения). Затрагиваются вертикальные срезы `Features/Outbound` и инфраструктурный слой
`Infrastructure`.

Состояние до изменения:

- `ChannelAuthType.ClientCertificate` существует в модели, но `ResolveAuthorizationHandler` отвечает
  `AUTH_TYPE_UNSUPPORTED`;
- `Program.cs` регистрирует один именованный `HttpClient` с `HttpClientHandler`;
- `ExecuteHttpCallHandler` не знает о канале, поэтому не может выбрать обработчик с нужным
  сертификатом.

## Компоненты

```text
Features/Outbound/ResolveAuthorization
    +-- IChannelCertificateStore          загрузка материала сертификата по config_key
    +-- AuthorizationResult               + ClientCertificate (X509Certificate2)

Infrastructure/Http
    +-- ChannelHttpClientKey              (BaseUrl, AuthType, CertificateThumbprint)
    +-- ChannelHttpClientPool             SocketsHttpHandler по ключу + срок жизни
    +-- IChannelHttpClientFactory         шов для тестов

Features/Outbound/ExecuteHttpCall
    +-- получает HttpClient из пула по ключу канала
```

## Ключ пула

`ChannelHttpClientKey` — запись из трёх полей:

```text
BaseUrl                абсолютный базовый адрес канала (без завершающего '/')
AuthType               none | bearer | api_key | basic | client_certificate
CertificateThumbprint  SHA-256 отпечаток клиентского сертификата либо null
```

Обоснование: сертификат предъявляется в TLS-рукопожатии, поэтому обработчик нельзя переиспользовать
между каналами с разными сертификатами (ADR §7.7). Тип авторизации входит в ключ, потому что
`client_certificate` меняет TLS-конфигурацию, а остальные типы — только заголовки; разделение по
типу исключает случайное переиспользование mTLS-обработчика для канала без сертификата.

## Пул обработчиков

- реализация — `ChannelHttpClientPool`, синглтон;
- внутри `ConcurrentDictionary<ChannelHttpClientKey, Entry>`, где `Entry` содержит
  `SocketsHttpHandler` и время создания;
- параметры обработчика: `AllowAutoRedirect = false`, `SslOptions.EnabledSslProtocols = Tls12 | Tls13`,
  `SslOptions.RemoteCertificateValidationCallback` не задаётся (проверка серверного сертификата
  всегда включена), `PooledConnectionLifetime` — 5 минут;
- для `client_certificate` в `SslOptions.ClientCertificates` добавляется загруженный сертификат;
- при обращении к ключу с истёкшим сроком жизни создаётся новый `Entry`; старый освобождается
  (`Dispose`) после прекращения использования;
- `HttpClient` создаётся на каждый вызов поверх обработчика из пула: `HttpClient` лёгкий, а
  обработчик задаёт пул соединений; `BaseAddress` не задаётся, используется абсолютный URI.

Ограничение: число записей пула ограничено числом различных ключей (каналы × ревизии с разными
сертификатами). Дополнительный предел не вводится; при большом числе ключей потребуется вытеснение,
которое фиксируется как открытый вопрос, а не реализуется вслепую.

## Материал клиентского сертификата

`IChannelCertificateStore` (реализация на `IConfiguration`) читает по `config_key`:

| Ключ конфигурации | Смысл |
|---|---|
| `<config_key>` | путь к PFX-файлу либо PFX в base64 |
| `<config_key>:Password` | пароль PFX; пустое значение допустимо для незащищённого файла |

Правила:

- пустой `config_key` или отсутствующее значение — отказ `SECRET_NOT_CONFIGURED`;
- PFX загружается с `X509KeyStorageFlags.EphemeralKeySet`, чтобы ключ не попадал в хранилище ОС;
- материал кэшируется по `config_key` на время жизни процесса: PFX разбирается один раз, один
  экземпляр сертификата используется всеми вызовами канала и освобождается при остановке
  приложения; обработчик пула сертификат не освобождает;
- у сертификата проверяется наличие приватного ключа и срок действия; истёкший сертификат —
  отказ `SECRET_NOT_CONFIGURED` с сообщением без значения материала;
- отпечаток считается как SHA-256 по `RawData` и используется только как ключ пула и для
  диагностики; сам материал в журнал и результат не попадает.

Следствие кэша: смена материала сертификата (файла, base64 или пароля) вступает в силу только
после перезапуска приложения (ADR §7.4).

Допущение: ADR не фиксирует имена ключей для пары «сертификат + пароль», поэтому используется
описанное соглашение; оно документируется в README и может быть заменено без изменения контракта.

## Поток вызова

```text
1. LoadActiveChannel            -> ActiveChannel (config_key, auth_type)
2. ResolveAuthorization         -> AuthorizationResult + X509Certificate2?
      client_certificate: IChannelCertificateStore.Load(config_key)
      ошибка -> отказ SECRET_NOT_CONFIGURED, HTTP-вызов не выполняется
3. ExecuteHttpCall
      key = ChannelHttpClientKey.From(channel, certificate)
      handler = pool.Get(key)
      client = new HttpClient(handler, disposeHandler: false)
      повторы, таймауты, Retry-After -- без изменений
4. OutboundResult               -> коды TRANSPORT_ERROR / HTTP_RESPONSE как раньше
```

## Ошибки и классификация

- отсутствие/непригодность материала сертификата — отказ шлюза до вызова,
  `outcome = GATEWAY_REFUSED`, код `SECRET_NOT_CONFIGURED`;
- ошибка TLS-рукопожатия — `outcome = TRANSPORT_ERROR`, код `TLS_FAILED` (существующая
  классификация §8.5/§8.6 не меняется);
- `AUTH_TYPE_UNSUPPORTED` остаётся только для значения `auth_type`, которого нет в перечислении
  (повреждённая конфигурация).

## Тестируемость

- unit-тест хранилища проверяет, что повторная загрузка одного ключа возвращает тот же экземпляр
  сертификата, а освобождение хранилища закрывает материал;
- `IChannelHttpClientFactory` позволяет подменить пул в интеграционных тестах и проверить, что
  вызову передан обработчик нужного ключа;
- unit-тесты пула проверяют: одинаковый ключ → тот же экземпляр обработчика, разный сертификат →
  разные экземпляры, истечение срока жизни → новый экземпляр;
- mTLS-сценарий проверяется на уровне выбора сертификата в `SslOptions.ClientCertificates`
  (без реального TLS-сервера), а также интеграционным тестом с тестовым HTTP-обработчиком;
- тест отказа не должен печатать материал сертификата и пароль.

## Влияние на контракты

- Protobuf-контракты не меняются;
- `OutboundResult` и коды ошибок не меняются;
- REST/gRPC управляющего API не меняется;
- существующие тесты авторизации обновляются: сценарий `client_certificate → AUTH_TYPE_UNSUPPORTED`
  заменяется на поддержку типа и отказ при отсутствии материала.

## Отклонения от ADR

Нет. Реализуется ровно §7.5 и §7.7. Открытый вопрос о вытеснении обработчиков из пула фиксируется
в README как ограничение текущего этапа.

