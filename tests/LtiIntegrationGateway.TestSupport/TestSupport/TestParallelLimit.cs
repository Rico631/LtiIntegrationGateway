using TUnit.Core.Interfaces;

namespace LtiIntegrationGateway.Tests.TestSupport;

/// <summary>
/// Предел параллельных тестов: каждый сценарий поднимает хост и шину Rebus, а контейнерный
/// PostgreSQL ограничен числом подключений (по умолчанию 100).
/// </summary>
public sealed record TestParallelLimit : IParallelLimit
{
    public int Limit => 6;
}
