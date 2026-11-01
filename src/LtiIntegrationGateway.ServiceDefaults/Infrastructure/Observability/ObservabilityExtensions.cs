using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Rebus.OpenTelemetry.Configuration;

namespace LtiIntegrationGateway.Infrastructure.Observability;

/// <summary>Регистрация наблюдаемости шлюза и OTLP-экспортёров.</summary>
public static class ObservabilityExtensions
{
    private static readonly double[] DurationBoundaries =
        [5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000, 10000];

    private static readonly double[] AttemptBoundaries = [1, 2, 3, 4, 5];

    /// <summary>
    /// Добавляет OTel traces/metrics и фоновый обновлятор backlog. При выключенной телеметрии
    /// регистрирует только общие сервисы и не создаёт OTLP-экспортёров.
    /// </summary>
    public static IServiceCollection AddGatewayObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        string? defaultServiceName = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var section = configuration.GetSection(OtelOptions.SectionName);
        var options = section.Get<OtelOptions>() ?? new OtelOptions();
        var unsetServiceName = new OtelOptions().ServiceName;

        // Aspire и коллектор задают endpoint стандартной переменной окружения.
        if (string.IsNullOrWhiteSpace(options.Endpoint))
        {
            options.Endpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        }

        if (!string.IsNullOrWhiteSpace(configuration["OTEL_SERVICE_NAME"]) &&
            options.ServiceName == unsetServiceName)
        {
            options.ServiceName = configuration["OTEL_SERVICE_NAME"]!;
        }
        else if (!string.IsNullOrWhiteSpace(defaultServiceName) && options.ServiceName == unsetServiceName)
        {
            // Имя сервиса отличает management и flow, когда переменная окружения не задана.
            options.ServiceName = defaultServiceName;
        }

        options.Validate();

        services.AddOptions<OtelOptions>().Bind(section);
        services.AddSingleton(options);
        services.AddSingleton<GatewayMetrics>();

        if (!options.Enabled)
        {
            return services;
        }

        var endpoint = new Uri(options.Endpoint!);

        services
            .AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    options.ServiceName,
                    serviceVersion: options.ServiceVersion,
                    autoGenerateServiceInstanceId: true)
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment"] = environment.EnvironmentName,
                }))
            .WithTracing(tracing => tracing
                .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(options.SamplingRatio)))
                .AddSource(GatewayTelemetry.ActivitySourceName)
                .AddAspNetCoreInstrumentation(instrumentation => instrumentation.RecordException = false)
                .AddHttpClientInstrumentation(instrumentation => instrumentation.RecordException = false)
                .AddRebusInstrumentation()
                .AddOtlpExporter(exporter =>
                {
                    exporter.Endpoint = endpoint;
                    exporter.Protocol = OtlpExportProtocol.Grpc;
                }))
            .WithMetrics(metrics => metrics
                .AddMeter(GatewayTelemetry.MeterName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddRebusInstrumentation()
                .AddView(GatewayMetrics.InboundDuration, new ExplicitBucketHistogramConfiguration
                {
                    Boundaries = DurationBoundaries,
                })
                .AddView(GatewayMetrics.OutboundDuration, new ExplicitBucketHistogramConfiguration
                {
                    Boundaries = DurationBoundaries,
                })
                .AddView(GatewayMetrics.OutboundAttempts, new ExplicitBucketHistogramConfiguration
                {
                    Boundaries = AttemptBoundaries,
                })
                .AddOtlpExporter((exporter, reader) =>
                {
                    exporter.Endpoint = endpoint;
                    exporter.Protocol = OtlpExportProtocol.Grpc;
                    reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds =
                        options.ExportIntervalSeconds * 1000;
                }));

        return services;
    }
}
