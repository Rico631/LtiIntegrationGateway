using System.Diagnostics;
using System.Diagnostics.Metrics;
using LtiIntegrationGateway.Infrastructure.Observability;

namespace LtiIntegrationGateway.Tests.TestSupport;

/// <summary>Запись измерения метрики: имя инструмента, значение и теги.</summary>
public sealed record MetricMeasurement(
    string Instrument,
    object Value,
    IReadOnlyDictionary<string, object?> Tags);

/// <summary>
/// Собирает Activity и измерения Meter без коллектора: используется для проверки
/// пользовательских spans и бизнес-метрик в тестах.
/// </summary>
public sealed class TelemetryCollector : IDisposable
{
    private readonly List<Activity> _activities = [];
    private readonly List<MetricMeasurement> _measurements = [];
    private readonly ActivityListener _activityListener;
    private readonly MeterListener _meterListener;
    private readonly Meter? _meterFilter;

    public TelemetryCollector(
        string activitySourceName = GatewayTelemetry.ActivitySourceName,
        string meterName = GatewayTelemetry.MeterName,
        Meter? meter = null)
    {
        _meterFilter = meter;
        _activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == activitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (_activities)
                {
                    _activities.Add(activity);
                }
            },
        };

        ActivitySource.AddActivityListener(_activityListener);

        _meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if ((_meterFilter is null && instrument.Meter.Name == meterName) ||
                    (_meterFilter is not null && ReferenceEquals(instrument.Meter, _meterFilter)))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };

        _meterListener.SetMeasurementEventCallback<long>(
            (instrument, value, tags, _) => Record(instrument, value, tags));
        _meterListener.SetMeasurementEventCallback<int>(
            (instrument, value, tags, _) => Record(instrument, value, tags));
        _meterListener.SetMeasurementEventCallback<double>(
            (instrument, value, tags, _) => Record(instrument, value, tags));
        _meterListener.Start();
    }

    /// <summary>Остановленные activity в порядке завершения.</summary>
    public IReadOnlyList<Activity> Activities
    {
        get
        {
            lock (_activities)
            {
                return _activities.ToArray();
            }
        }
    }

    /// <summary>Измерения метрик в порядке записи.</summary>
    public IReadOnlyList<MetricMeasurement> Measurements
    {
        get
        {
            lock (_measurements)
            {
                return _measurements.ToArray();
            }
        }
    }

    /// <summary>Activity с указанным именем.</summary>
    public IReadOnlyList<Activity> ActivitiesNamed(string name) =>
        Activities.Where(activity => activity.DisplayName == name).ToArray();

    /// <summary>Измерения указанного инструмента.</summary>
    public IReadOnlyList<MetricMeasurement> MeasurementsNamed(string name) =>
        Measurements.Where(measurement => measurement.Instrument == name).ToArray();

    /// <summary>Считывает наблюдаемые инструменты (observable gauge).</summary>
    public void RecordObservableInstruments() => _meterListener.RecordObservableInstruments();

    /// <inheritdoc />
    public void Dispose()
    {
        _meterListener.Dispose();
        _activityListener.Dispose();
    }

    private void Record(Instrument instrument, object value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var copiedTags = tags.ToArray()
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        lock (_measurements)
        {
            _measurements.Add(new MetricMeasurement(instrument.Name, value, copiedTags));
        }
    }
}
