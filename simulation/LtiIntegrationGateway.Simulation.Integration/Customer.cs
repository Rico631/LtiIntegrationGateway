namespace LtiIntegrationGateway.Simulation.Integration;

/// <summary>Customer, который шлюз передаёт внешней системе в исходящем контуре.</summary>
public sealed record Customer(string Name);

/// <summary>Ответ внешней системы на приём customer.</summary>
public sealed record CustomerResponse(string Status, string Message);
