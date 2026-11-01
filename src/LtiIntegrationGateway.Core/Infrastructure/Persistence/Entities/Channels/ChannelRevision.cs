namespace LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;

/// <summary>Ревизия конфигурации канала: черновик, опубликованная или активная версия настроек.</summary>
/// <remarks>
/// Настройки живут в ревизии, а не в канале: опубликованная ревизия неизменяема, а откат
/// возвращает предыдущий набор настроек без копирования полей.
/// </remarks>
public sealed class ChannelRevision : IEntity
{
    /// <summary>Уникальный идентификатор записи.</summary>
    public Guid Id { get; set; }

    /// <summary>Идентификатор исходящего канала.</summary>
    public Guid OutboundChannelId { get; set; }

    /// <summary>Навигационное свойство исходящего канала.</summary>
    public OutboundChannel? OutboundChannel { get; set; }

    /// <summary>Номер ревизии в рамках канала, начиная с 1.</summary>
    public int Number { get; set; }

    /// <summary>Состояние записи.</summary>
    public ChannelRevisionStatus Status { get; set; }

    /// <summary>Базовый адрес внешней системы: абсолютный <c>https</c> без учётных данных.</summary>
    public required string BaseUrl { get; set; }

    /// <summary>Тип авторизации.</summary>
    public ChannelAuthType AuthType { get; set; }

    /// <summary>Ключ конфигурации, из которого берётся значение секрета. Значение не хранится.</summary>
    public string? ConfigKey { get; set; }

    /// <summary>Имя заголовка или query-параметра для <see cref="ChannelAuthType.ApiKey"/>.</summary>
    public string? HeaderName { get; set; }

    /// <summary>Имя пользователя для <see cref="ChannelAuthType.Basic"/>.</summary>
    public string? UserName { get; set; }

    /// <summary>Таймаут одной попытки в миллисекундах.</summary>
    public int PerAttemptTimeoutMs { get; set; }

    /// <summary>Общий предел длительности вызова в миллисекундах.</summary>
    public int OverallCapMs { get; set; }

    /// <summary>Политика повторов в виде JSON-документа.</summary>
    public RetryPolicy RetryPolicy { get; set; } = RetryPolicy.Empty;

    /// <summary>Момент создания записи в UTC.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
