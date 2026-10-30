namespace LtiIntegrationGateway.Infrastructure.Persistence.Entities;

/// <summary>Разрешённый шаблон относительного пути канала.</summary>
public sealed class ChannelAllowedPath
{
    public Guid Id { get; set; }

    public Guid ChannelRevisionId { get; set; }

    public ChannelRevision? ChannelRevision { get; set; }

    /// <summary>Шаблон пути, например <c>/api/contracts/{id}</c>.</summary>
    public required string Template { get; set; }
}
