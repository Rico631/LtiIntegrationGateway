namespace LtiIntegrationGateway.Infrastructure.Persistence.Entities;

/// <summary>Разрешённый HTTP-метод канала.</summary>
public sealed class ChannelAllowedMethod
{
    public Guid Id { get; set; }

    public Guid ChannelRevisionId { get; set; }

    public ChannelRevision? ChannelRevision { get; set; }

    /// <summary>HTTP-метод в верхнем регистре, например <c>POST</c>.</summary>
    public required string Method { get; set; }
}
