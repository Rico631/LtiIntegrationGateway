namespace LtiIntegrationGateway.Infrastructure.Persistence.Entities;

/// <summary>Сущность хранилища с уникальным идентификатором.</summary>
public interface IEntity
{
    /// <summary>Уникальный идентификатор записи.</summary>
    public Guid Id { get; set; }
}
