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
}
