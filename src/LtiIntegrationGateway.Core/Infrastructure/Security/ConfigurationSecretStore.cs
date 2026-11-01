namespace LtiIntegrationGateway.Infrastructure.Security;

/// <summary>
/// Хранилище секретов поверх <see cref="IConfiguration"/>: значения берутся из переменных
/// окружения, в базе остаются только имена ключей.
/// </summary>
public sealed class ConfigurationSecretStore(IConfiguration configuration) : ISecretStore
{
    public string? Find(string configKey)
    {
        if (string.IsNullOrWhiteSpace(configKey))
        {
            return null;
        }

        var value = configuration[configKey];

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
