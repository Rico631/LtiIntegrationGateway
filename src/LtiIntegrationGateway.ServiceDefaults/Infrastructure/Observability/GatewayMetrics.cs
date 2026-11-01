using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace LtiIntegrationGateway.Infrastructure.Observability;

/// <summary>Бизнес-метрики шлюза с ограниченным набором измерений.</summary>
public sealed class GatewayMetrics : IDisposable
{
    /// <summary>Счётчик входящих запросов.</summary>
    public const string InboundRequests = "lti.gateway.inbound.requests";

    /// <summary>Гистограмма длительности входящих запросов.</summary>
    public const string InboundDuration = "lti.gateway.inbound.duration";

    /// <summary>Счётчик завершённых исходящих операций.</summary>
    public const string OutboundOperations = "lti.gateway.outbound.operations";

    /// <summary>Гистограмма длительности исходящих операций.</summary>
    public const string OutboundDuration = "lti.gateway.outbound.duration";

    /// <summary>Гистограмма числа выполненных попыток.</summary>
    public const string OutboundAttempts = "lti.gateway.outbound.attempts";

    /// <summary>Счётчик доставки сохранённых результатов.</summary>
    public const string OutboundDeliveries = "lti.gateway.outbound.deliveries";

    /// <summary>Наблюдаемый gauge ожидающих доставки результатов.</summary>
    public const string OutboundPending = "lti.gateway.outbound.pending";

    /// <summary>Наблюдаемый gauge применённой версии конфигурации интеграции.</summary>
    public const string ConfigurationAppliedVersion = "lti.gateway.config.applied_version";

    /// <summary>Наблюдаемый gauge отставания применённой версии от действующей.</summary>
    public const string ConfigurationLag = "lti.gateway.config.lag";

    /// <summary>Ключ измерения канала.</summary>
    public const string ChannelTag = GatewayTelemetry.ChannelTag;

    /// <summary>Ключ измерения идентификатора продукта.</summary>
    public const string ProductIdTag = GatewayTelemetry.ProductIdTag;

    /// <summary>Ключ измерения HTTP-метода.</summary>
    public const string HttpMethodTag = GatewayTelemetry.HttpMethodTag;

    /// <summary>Ключ измерения шаблона маршрута.</summary>
    public const string HttpRouteTag = GatewayTelemetry.HttpRouteTag;

    /// <summary>Ключ измерения HTTP-статуса.</summary>
    public const string HttpStatusCodeTag = GatewayTelemetry.HttpStatusCodeTag;

    /// <summary>Ключ измерения кода ошибки.</summary>
    public const string ErrorCodeTag = GatewayTelemetry.ErrorCodeTag;

    /// <summary>Ключ измерения исхода.</summary>
    public const string OutcomeTag = GatewayTelemetry.OutcomeTag;

    /// <summary>Ключ измерения интеграции.</summary>
    public const string IntegrationTag = GatewayTelemetry.IntegrationTag;

    private readonly Meter _meter;
    private readonly Counter<long> _inboundRequests;
    private readonly Histogram<double> _inboundDuration;
    private readonly Counter<long> _outboundOperations;
    private readonly Histogram<double> _outboundDuration;
    private readonly Histogram<int> _outboundAttempts;
    private readonly Counter<long> _outboundDeliveries;
    private long _pending;
    private long _appliedVersion;
    private long _configurationLag;

    /// <summary>Meter с инструментами бизнес-метрик.</summary>
    public Meter Meter => _meter;

    /// <summary>Создаёт инструменты в Meter шлюза.</summary>
    public GatewayMetrics()
    {
        _meter = new Meter(GatewayTelemetry.MeterName, GatewayTelemetry.Version);
        _inboundRequests = _meter.CreateCounter<long>(
            InboundRequests, description: "Количество входящих запросов шлюза.");
        _inboundDuration = _meter.CreateHistogram<double>(
            InboundDuration, unit: "ms", description: "Длительность входящих запросов.");
        _outboundOperations = _meter.CreateCounter<long>(
            OutboundOperations, description: "Количество завершённых исходящих операций.");
        _outboundDuration = _meter.CreateHistogram<double>(
            OutboundDuration, unit: "ms", description: "Длительность исходящих операций.");
        _outboundAttempts = _meter.CreateHistogram<int>(
            OutboundAttempts, description: "Количество выполненных попыток внешнего вызова.");
        _outboundDeliveries = _meter.CreateCounter<long>(
            OutboundDeliveries, description: "Количество попыток доставки результатов.");
        _meter.CreateObservableGauge(
            OutboundPending,
            () => Volatile.Read(ref _pending),
            description: "Количество результатов, ожидающих доставки.");
        _meter.CreateObservableGauge(
            ConfigurationAppliedVersion,
            () => Volatile.Read(ref _appliedVersion),
            description: "Применённая версия конфигурации интеграции.");
        _meter.CreateObservableGauge(
            ConfigurationLag,
            () => Volatile.Read(ref _configurationLag),
            description: "Отставание применённой версии конфигурации от действующей.");
    }

    /// <summary>Записывает результат входящего запроса.</summary>
    public void RecordInbound(
        string channel,
        string httpMethod,
        string httpRoute,
        int httpStatusCode,
        string? errorCode,
        double durationMilliseconds)
    {
        var tags = new TagList();
        AddIntegration(ref tags);
        AddTag(ref tags, ChannelTag, channel);
        AddTag(ref tags, HttpMethodTag, httpMethod);
        AddTag(ref tags, HttpRouteTag, httpRoute);
        tags.Add(HttpStatusCodeTag, httpStatusCode);
        AddTag(ref tags, ErrorCodeTag, errorCode);

        _inboundRequests.Add(1, tags);
        _inboundDuration.Record(durationMilliseconds, tags);
    }

    /// <summary>Записывает результат исходящей операции.</summary>
    public void RecordOutbound(
        string productId,
        string channel,
        string outcome,
        string? errorCode,
        double durationMilliseconds,
        int attemptCount)
    {
        var tags = new TagList();
        AddIntegration(ref tags);
        AddTag(ref tags, ProductIdTag, productId);
        AddTag(ref tags, ChannelTag, channel);
        AddTag(ref tags, OutcomeTag, outcome);
        AddTag(ref tags, ErrorCodeTag, errorCode);

        _outboundOperations.Add(1, tags);
        _outboundDuration.Record(durationMilliseconds, tags);
        _outboundAttempts.Record(attemptCount, tags);
    }

    /// <summary>Записывает попытку доставки сохранённого результата.</summary>
    public void RecordDelivery(string channel, bool succeeded)
    {
        var tags = new TagList();
        AddIntegration(ref tags);
        AddTag(ref tags, ChannelTag, channel);
        AddTag(ref tags, OutcomeTag, succeeded ? "delivered" : "failed");

        _outboundDeliveries.Add(1, tags);
    }

    /// <summary>Обновляет значение наблюдаемого gauge ожидающих результатов.</summary>
    public void SetPending(long value) => Volatile.Write(ref _pending, value);

    /// <summary>Обновляет метрики применённой версии конфигурации и отставания от действующей.</summary>
    public void SetConfiguration(long appliedVersion, long desiredVersion)
    {
        Volatile.Write(ref _appliedVersion, appliedVersion);
        Volatile.Write(ref _configurationLag, Math.Max(0, desiredVersion - appliedVersion));
    }

    /// <inheritdoc />
    public void Dispose() => _meter.Dispose();

    private static void AddIntegration(ref TagList tags) => AddTag(ref tags, IntegrationTag, GatewayTelemetry.Integration);

    private static void AddTag(ref TagList tags, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            tags.Add(name, value);
        }
    }
}
