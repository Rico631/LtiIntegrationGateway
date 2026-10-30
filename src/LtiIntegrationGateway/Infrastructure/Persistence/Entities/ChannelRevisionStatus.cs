namespace LtiIntegrationGateway.Infrastructure.Persistence.Entities;

/// <summary>Состояние ревизии конфигурации канала.</summary>
public enum ChannelRevisionStatus
{
    /// <summary>Черновик: единственная редактируемая ревизия канала.</summary>
    Draft = 1,

    /// <summary>Опубликована и неизменяема, но ещё не активна.</summary>
    Published = 2,

    /// <summary>Действующая конфигурация канала.</summary>
    Active = 3,

    /// <summary>Ранее активная ревизия, заменённая новой.</summary>
    Superseded = 4,
}
