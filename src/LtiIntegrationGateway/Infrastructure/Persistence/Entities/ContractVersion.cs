namespace LtiIntegrationGateway.Infrastructure.Persistence.Entities;

/// <summary>Формат исходного документа контракта.</summary>
public enum ContractSourceFormat
{
    OpenApi30 = 1,
    Swagger20 = 2,
}

/// <summary>Состояние версии контракта.</summary>
public enum ContractVersionStatus
{
    Draft = 1,
    Published = 2,
    Active = 3,
    Superseded = 4,
}

/// <summary>Версия OpenAPI-контракта канала; опубликованные версии неизменяемы.</summary>
public sealed class ContractVersion
{
    public Guid Id { get; set; }

    public Guid ChannelId { get; set; }

    public Channel? Channel { get; set; }

    /// <summary>Номер версии внутри канала, начиная с 1.</summary>
    public int Version { get; set; }

    public ContractSourceFormat SourceFormat { get; set; }

    /// <summary>Исходный документ без изменений.</summary>
    public required string Document { get; set; }

    public ContractVersionStatus Status { get; set; }

    /// <summary>Очередь получателя продуктов; обязательна при публикации версии.</summary>
    public string? RecipientQueue { get; set; }

    /// <summary>Тип аутентификации входящих вызовов: none, api_key, basic, client_certificate.</summary>
    public ChannelAuthType AuthType { get; set; }

    /// <summary>Ключ конфигурации, из которого берётся значение секрета входящего вызова.</summary>
    public string? ConfigKey { get; set; }

    /// <summary>Имя заголовка для типа авторизации api_key.</summary>
    public string? HeaderName { get; set; }

    /// <summary>Имя пользователя для типа авторизации basic.</summary>
    public string? UserName { get; set; }

    /// <summary>Квота запросов на окно; null — используется общая квота приложения.</summary>
    public int? RateLimitPermit { get; set; }

    /// <summary>Окно квоты в секундах; null — используется общее окно приложения.</summary>
    public int? RateLimitWindowSeconds { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public List<ContractOperation> Operations { get; set; } = [];
}

/// <summary>Операция опубликованной версии контракта.</summary>
public sealed class ContractOperation
{
    public Guid Id { get; set; }

    public Guid ContractVersionId { get; set; }

    public ContractVersion? ContractVersion { get; set; }

    public required string HttpMethod { get; set; }

    public required string Template { get; set; }

    public string? OperationId { get; set; }

    /// <summary>
    /// Схемы тела операции по каждому объявленному Content-Type в виде JSON-объекта
    /// <c>{ "application/json": { ... } }</c>; локальные <c>$ref</c> уже развёрнуты.
    /// </summary>
    public string? RequestSchemas { get; set; }

    /// <summary>Признак обслуживания операции: отключённая операция отвечает 404 и пишется в аудит.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Переопределение очереди получателя; null — очередь версии контракта.</summary>
    public string? RecipientQueue { get; set; }

    /// <summary>Переопределение квоты запросов на окно; null — квота версии или приложения.</summary>
    public int? RateLimitPermit { get; set; }

    /// <summary>Переопределение окна квоты в секундах; null — окно версии или приложения.</summary>
    public int? RateLimitWindowSeconds { get; set; }
}
