using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Contracts;

namespace LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;

/// <summary>Режим входящего канала: асинхронный или синхронный request/reply.</summary>
public enum InboundResponseMode
{
    /// <summary>Шлюз отвечает 202 после передачи сообщения и не ждёт ответа продукта.</summary>
    Async = 1,

    /// <summary>Шлюз ожидает InboundResponse и возвращает ответ продукта внешнему клиенту.</summary>
    Sync = 2,
}

/// <summary>Зарегистрированный входящий канал интеграции с внешней системой.</summary>
public sealed class InboundChannel : IEntity
{
    /// <summary>Уникальный идентификатор записи.</summary>
    public Guid Id { get; set; }

    /// <summary>Уникальное имя канала в пределах inbound-направления, например <c>bank-a</c>.</summary>
    public required string Name { get; set; }

    /// <summary>Режим обработки всех входящих endpoints канала.</summary>
    public InboundResponseMode ResponseMode { get; set; } = InboundResponseMode.Async;

    /// <summary>Таймаут ожидания InboundResponse в миллисекундах; положительное значение.</summary>
    public int InboundResponseTimeoutMs { get; set; } = 30_000;

    /// <summary>Момент создания записи в UTC.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Версии OpenAPI-контракта входящего канала.</summary>
    public List<ContractVersion> ContractVersions { get; set; } = [];
}
