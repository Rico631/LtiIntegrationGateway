namespace LtiIntegrationGateway.Infrastructure.Security;

/// <summary>Источник значений секретов по имени ключа конфигурации.</summary>
public interface ISecretStore
{
    /// <summary>Возвращает значение секрета или <c>null</c>, если ключ не настроен.</summary>
    string? Find(string configKey);
}
