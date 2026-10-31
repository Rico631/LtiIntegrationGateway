using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;

namespace LtiIntegrationGateway.Infrastructure.Persistence.Entities.Contracts;

/// <summary>Формат исходного документа контракта.</summary>
public enum ContractSourceFormat
{
    /// <summary>Документ OpenAPI 3.0.</summary>
    OpenApi30 = 1,

    /// <summary>Документ Swagger 2.0, сконвертированный при загрузке.</summary>
    Swagger20 = 2,
}

/// <summary>Состояние версии контракта.</summary>
public enum ContractVersionStatus
{
    /// <summary>Черновик версии контракта.</summary>
    Draft = 1,

    /// <summary>Опубликованная неизменяемая версия.</summary>
    Published = 2,

    /// <summary>Действующая версия контракта.</summary>
    Active = 3,

    /// <summary>Ранее активная версия, заменённая новой.</summary>
    Superseded = 4,
}

/// <summary>Версия OpenAPI-контракта канала; опубликованные версии неизменяемы.</summary>
public sealed class ContractVersion : IEntity
{
    /// <summary>Уникальный идентификатор записи.</summary>
    public Guid Id { get; set; }

    /// <summary>Идентификатор канала.</summary>
    public Guid ChannelId { get; set; }

    /// <summary>Навигационное свойство канала.</summary>
    public Channel? Channel { get; set; }

    /// <summary>Номер версии внутри канала, начиная с 1.</summary>
    public int Version { get; set; }

    /// <summary>Формат исходного документа.</summary>
    public ContractSourceFormat SourceFormat { get; set; }

    /// <summary>Исходный документ без изменений.</summary>
    public required string Document { get; set; }

    /// <summary>Состояние записи.</summary>
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

    /// <summary>Момент создания записи в UTC.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Момент публикации версии.</summary>
    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>Момент активации версии.</summary>
    public DateTimeOffset? ActivatedAt { get; set; }

    /// <summary>Операции версии контракта.</summary>
    public List<ContractOperation> Operations { get; set; } = [];
}

/// <summary>Операция опубликованной версии контракта.</summary>
public sealed class ContractOperation : IEntity
{
    /// <summary>Уникальный идентификатор записи.</summary>
    public Guid Id { get; set; }

    /// <summary>Идентификатор версии контракта.</summary>
    public Guid ContractVersionId { get; set; }

    /// <summary>Навигационное свойство версии контракта.</summary>
    public ContractVersion? ContractVersion { get; set; }

    /// <summary>HTTP-метод запроса.</summary>
    public required string HttpMethod { get; set; }

    /// <summary>Шаблон относительного пути.</summary>
    public required string Template { get; set; }

    /// <summary>Идентификатор операции из OpenAPI.</summary>
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
}
