namespace LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;

/// <summary>Тип авторизации исходящих вызовов канала.</summary>
public enum ChannelAuthType
{
    /// <summary>Авторизация не требуется.</summary>
    None = 1,

    /// <summary>Bearer-токен в заголовке Authorization.</summary>
    Bearer = 2,

    /// <summary>API-ключ в заголовке или query-параметре.</summary>
    ApiKey = 3,

    /// <summary>HTTP Basic с именем пользователя и паролем.</summary>
    Basic = 4,

    /// <summary>Клиентский сертификат в TLS-рукопожатии.</summary>
    ClientCertificate = 5,
}
