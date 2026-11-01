namespace LtiIntegrationGateway.Infrastructure.Management;

/// <summary>Настройки управляющего API: ключ доступа и имя заголовка.</summary>
public sealed class ManagementApiKeyOptions
{
    public const string SectionName = "Management";

    /// <summary>Значение ключа; при пустом значении управляющий API отвечает <c>503</c>.</summary>
    public string? ApiKey { get; set; }

    public string HeaderName { get; set; } = "X-Management-Api-Key";
}
