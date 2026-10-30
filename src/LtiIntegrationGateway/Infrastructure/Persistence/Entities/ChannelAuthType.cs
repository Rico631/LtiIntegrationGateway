namespace LtiIntegrationGateway.Infrastructure.Persistence.Entities;

/// <summary>Тип авторизации исходящих вызовов канала.</summary>
public enum ChannelAuthType
{
    None = 1,
    Bearer = 2,
    ApiKey = 3,
    Basic = 4,
    ClientCertificate = 5,
}
