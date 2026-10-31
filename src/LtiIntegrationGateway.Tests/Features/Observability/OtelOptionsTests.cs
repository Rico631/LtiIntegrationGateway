using LtiIntegrationGateway.Infrastructure.Observability;

namespace LtiIntegrationGateway.Tests.Features.Observability;

/// <summary>Проверка значений по умолчанию и fail-fast конфигурации OTel.</summary>
public class OtelOptionsTests
{
    [Test]
    public async Task Defaults_disable_export_and_use_documented_values()
    {
        var options = new OtelOptions();

        await Assert.That(options.Enabled).IsFalse();
        await Assert.That(options.Endpoint).IsNull();
        await Assert.That(options.ServiceName).IsEqualTo("LtiIntegrationGateway");
        await Assert.That(options.SamplingRatio).IsEqualTo(1.0);
        await Assert.That(options.ExportIntervalSeconds).IsEqualTo(60);
        await Assert.That(options.ServiceVersion).IsNotEmpty();
    }

    [Test]
    public async Task Disabled_telemetry_does_not_require_endpoint_or_validate_export_settings()
    {
        var options = new OtelOptions
        {
            Enabled = false,
            SamplingRatio = 2,
            ExportIntervalSeconds = 0,
        };

        options.Validate();

        await Assert.That(options.Enabled).IsFalse();
    }

    [Test]
    public async Task Invalid_endpoint_reports_key_without_value()
    {
        var options = new OtelOptions
        {
            Enabled = true,
            Endpoint = "collector:4317",
        };

        var exception = await InvalidAsync(() => options.Validate());

        await Assert.That(exception.Message).Contains("Observability:Otel:Endpoint");
        await Assert.That(exception.Message).DoesNotContain("collector:4317");
    }

    [Test]
    public async Task Invalid_sampling_ratio_reports_key()
    {
        var options = new OtelOptions
        {
            Enabled = true,
            Endpoint = "http://collector:4317",
            SamplingRatio = 1.1,
        };

        var exception = await InvalidAsync(() => options.Validate());

        await Assert.That(exception.Message).Contains("Observability:Otel:SamplingRatio");
        await Assert.That(exception.Message).DoesNotContain("1.1");
    }

    [Test]
    public async Task Invalid_export_interval_reports_key()
    {
        var options = new OtelOptions
        {
            Enabled = true,
            Endpoint = "http://collector:4317",
            ExportIntervalSeconds = 0,
        };

        var exception = await InvalidAsync(() => options.Validate());

        await Assert.That(exception.Message).Contains("Observability:Otel:ExportIntervalSeconds");
    }

    [Test]
    public async Task Invalid_service_name_reports_key()
    {
        var options = new OtelOptions
        {
            Enabled = true,
            Endpoint = "http://collector:4317",
            ServiceName = " ",
        };

        var exception = await InvalidAsync(() => options.Validate());

        await Assert.That(exception.Message).Contains("Observability:Otel:ServiceName");
    }

    [Test]
    public async Task Valid_enabled_values_pass_validation()
    {
        var options = new OtelOptions
        {
            Enabled = true,
            Endpoint = "https://collector.example:4317",
            ServiceName = "gateway-test",
            SamplingRatio = 0.25,
            ExportIntervalSeconds = 1,
        };

        options.Validate();

        await Assert.That(options.Enabled).IsTrue();
    }

    private static async Task<InvalidOperationException> InvalidAsync(Action action)
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            action();

            return Task.CompletedTask;
        });

        return exception!;
    }
}
