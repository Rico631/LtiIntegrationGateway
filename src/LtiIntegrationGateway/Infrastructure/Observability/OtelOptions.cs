namespace LtiIntegrationGateway.Infrastructure.Observability;

/// <summary>Настройки экспорта OpenTelemetry из секции <c>Observability:Otel</c>.</summary>
public sealed class OtelOptions
{
    /// <summary>Имя секции конфигурации.</summary>
    public const string SectionName = "Observability:Otel";

    /// <summary>Включает создание OTLP-экспортёров; по умолчанию выключено.</summary>
    public bool Enabled { get; set; }

    /// <summary>Абсолютный URI OTLP-коллектора; обязателен при <see cref="Enabled"/>.</summary>
    public string? Endpoint { get; set; }

    /// <summary>Имя сервиса в OTel-ресурсе.</summary>
    public string ServiceName { get; set; } = "LtiIntegrationGateway";

    /// <summary>Версия сервиса в OTel-ресурсе.</summary>
    public string ServiceVersion { get; set; } = GatewayTelemetry.Version;

    /// <summary>Коэффициент сэмплирования трассировок от 0 до 1 включительно.</summary>
    public double SamplingRatio { get; set; } = 1.0;

    /// <summary>Интервал экспорта метрик в секундах от 1 до 3600.</summary>
    public int ExportIntervalSeconds { get; set; } = 2;

    /// <summary>
    /// Проверяет параметры при включённой телеметрии. Сообщение содержит только имя
    /// некорректного ключа и не раскрывает значение настройки.
    /// </summary>
    public void Validate()
    {
        if (!Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Endpoint) ||
            !Uri.TryCreate(Endpoint, UriKind.Absolute, out var endpoint) ||
            (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                "Некорректная конфигурация Observability:Otel:Endpoint: " +
                "требуется абсолютный http/https URI.");
        }

        if (string.IsNullOrWhiteSpace(ServiceName))
        {
            throw new InvalidOperationException(
                "Некорректная конфигурация Observability:Otel:ServiceName: требуется непустая строка.");
        }

        if (double.IsNaN(SamplingRatio) || SamplingRatio is < 0 or > 1)
        {
            throw new InvalidOperationException(
                "Некорректная конфигурация Observability:Otel:SamplingRatio: " +
                "значение должно быть от 0 до 1.");
        }

        if (ExportIntervalSeconds is < 1 or > 3600)
        {
            throw new InvalidOperationException(
                "Некорректная конфигурация Observability:Otel:ExportIntervalSeconds: " +
                "значение должно быть от 1 до 3600.");
        }
    }
}
