using LtiIntegrationGateway.Infrastructure.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace LtiIntegrationGateway.Tests.Features.Observability;

/// <summary>Проверка условной регистрации OTel-провайдеров.</summary>
public class ObservabilityRegistrationTests
{
    [Test]
    public async Task Disabled_telemetry_does_not_create_providers()
    {
        using var provider = Build(
            new Dictionary<string, string?>
            {
                ["Observability:Otel:Enabled"] = "false",
            });

        await Assert.That(provider.GetService<TracerProvider>()).IsNull();
        await Assert.That(provider.GetService<MeterProvider>()).IsNull();
    }

    [Test]
    public async Task Enabled_telemetry_registers_both_providers()
    {
        using var provider = Build(
            new Dictionary<string, string?>
            {
                ["Observability:Otel:Enabled"] = "true",
                ["Observability:Otel:Endpoint"] = "http://localhost:4317",
                ["Observability:Otel:SamplingRatio"] = "0.5",
                ["Observability:Otel:ExportIntervalSeconds"] = "60",
            });

        await Assert.That(provider.GetService<TracerProvider>()).IsNotNull();
        await Assert.That(provider.GetService<MeterProvider>()).IsNotNull();
    }

    [Test]
    public async Task Invalid_enabled_configuration_fails_before_provider_creation()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            _ = Build(
                new Dictionary<string, string?>
                {
                    ["Observability:Otel:Enabled"] = "true",
                    ["Observability:Otel:Endpoint"] = "not-an-absolute-uri",
                });
            return Task.CompletedTask;
        });

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("Observability:Otel:Endpoint");
    }

    private static ServiceProvider Build(IReadOnlyDictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGatewayObservability(configuration, new TestHostEnvironment());

        return services.BuildServiceProvider();
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";

        public string ApplicationName { get; set; } = "LtiIntegrationGateway.Tests";

        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
