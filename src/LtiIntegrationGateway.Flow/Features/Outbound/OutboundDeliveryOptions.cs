namespace LtiIntegrationGateway.Features.Outbound;

/// <summary>Параметры фонового диспетчера недоставленных результатов.</summary>
/// <remarks>
/// Значения — эксплуатационные, а не часть контракта: интервал и размер пачки подбираются под
/// нагрузку. Отключение службы оставляет записи <c>pending</c> без изменений: их доставит следующий
/// запуск с включённым диспетчером.
/// </remarks>
public sealed class OutboundDeliveryOptions
{
    public const string SectionName = "Outbound:DeliveryDispatcher";

    /// <summary>Минимальный интервал между циклами, секунды.</summary>
    public const int MinIntervalSeconds = 1;

    /// <summary>Минимальный размер пачки записей за цикл.</summary>
    public const int MinBatchSize = 1;

    /// <summary>Максимальный размер пачки записей за цикл.</summary>
    public const int MaxBatchSize = 500;

    /// <summary>Запускать ли фоновую службу; по умолчанию включена.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Пауза между циклами в секундах; допустимо от 1.</summary>
    public int IntervalSeconds { get; set; } = 15;

    /// <summary>Число записей, обрабатываемых за цикл; допустимо от 1 до 500.</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>Проверяет диапазоны значений; некорректная конфигурация останавливает запуск приложения.</summary>
    public void Validate()
    {
        if (IntervalSeconds < MinIntervalSeconds)
        {
            throw new InvalidOperationException(
                $"'{SectionName}:IntervalSeconds' должен быть не меньше {MinIntervalSeconds}, " +
                $"а задано {IntervalSeconds}.");
        }

        if (BatchSize is < MinBatchSize or > MaxBatchSize)
        {
            throw new InvalidOperationException(
                $"'{SectionName}:BatchSize' должен быть в диапазоне {MinBatchSize}..{MaxBatchSize}, " +
                $"а задано {BatchSize}.");
        }
    }
}
