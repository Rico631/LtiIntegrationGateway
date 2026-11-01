namespace LtiIntegrationGateway.Tests.TestSupport;

/// <summary>Управляемое время для проверки срока жизни записей пула и сертификатов.</summary>
public sealed class ManualTimeProvider : TimeProvider
{
    /// <summary>Текущее время провайдера; тест задаёт его явно или сдвигает через <see cref="Advance"/>.</summary>
    public DateTimeOffset UtcNow { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Действие, выполняемое при каждом обращении к времени: шов для проверки гонок.</summary>
    public Action? OnGetUtcNow { get; set; }

    public override DateTimeOffset GetUtcNow()
    {
        OnGetUtcNow?.Invoke();

        return UtcNow;
    }

    public void Advance(TimeSpan delta) => UtcNow += delta;
}

