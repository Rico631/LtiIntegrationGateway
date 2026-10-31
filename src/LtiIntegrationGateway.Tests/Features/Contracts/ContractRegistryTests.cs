using System.Net;
using System.Text.Json;
using LtiIntegrationGateway.Contracts.Management.V1;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LtiIntegrationGateway.Tests.Features.Contracts;

public class ContractRegistryTests
{
    private static StringContent ChannelBody(string name) =>
        ManagementApi.Json(JsonSerializer.Serialize(new
        {
            name,
            settings = new
            {
                baseUrl = "https://bank-a.example",
                authType = "none",
                perAttemptTimeoutMs = 5000,
                overallCapMs = 30000,
                retryPolicy = new[]
                {
                    new { match = "transport_error", statusCodes = Array.Empty<int>(), delaysMs = new[] { 100 } },
                },
            },
        }));

    private static async Task CreateChannelAsync(HttpClient client, string name)
    {
        var response = await client.PostAsync("/v1/channels", ChannelBody(name));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    private static async Task<ContractOperationResponse> UploadAsync(
        ManagementService.ManagementServiceClient client,
        string channelName,
        string document) =>
        await client.UploadContractAsync(
            new UploadContractRequest { ChannelName = channelName, Document = document },
            ManagementApi.Call());

    [Test]
    public async Task OpenApi_30_upload_creates_draft_version()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var http = ManagementApi.CreateClient(factory);
        await CreateChannelAsync(http, "bank-a");

        using var channel = ManagementApi.CreateChannel(factory);
        var grpc = new ManagementService.ManagementServiceClient(channel);

        var upload = await UploadAsync(grpc, "bank-a", OpenApiSamples.OpenApi30WithPath("POST", "/callbacks/contracts"));

        await Assert.That(upload.Succeeded).IsTrue();
        await Assert.That(upload.Version).IsEqualTo(1);
        await Assert.That(upload.SourceFormat).IsEqualTo("OpenApi30");
    }

    [Test]
    public async Task Swagger_20_upload_is_accepted_and_marked_in_audit()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var http = ManagementApi.CreateClient(factory);
        await CreateChannelAsync(http, "bank-a");

        using var channel = ManagementApi.CreateChannel(factory);
        var grpc = new ManagementService.ManagementServiceClient(channel);

        var upload = await UploadAsync(grpc, "bank-a", OpenApiSamples.Swagger20);

        await Assert.That(upload.Succeeded).IsTrue();
        await Assert.That(upload.SourceFormat).IsEqualTo("Swagger20");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
        var entries = await db.ManagementAuditEntries.Where(entry => entry.Operation == "UploadContract").ToListAsync();

        await Assert.That(entries.Any(entry =>
            (entry.Details ?? string.Empty).Contains("swagger_2_0_converted", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task OpenApi_31_upload_is_rejected()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var http = ManagementApi.CreateClient(factory);
        await CreateChannelAsync(http, "bank-a");

        using var channel = ManagementApi.CreateChannel(factory);
        var grpc = new ManagementService.ManagementServiceClient(channel);

        var upload = await UploadAsync(grpc, "bank-a", OpenApiSamples.OpenApi31);

        await Assert.That(upload.Succeeded).IsFalse();
        await Assert.That(upload.Problems.Any(problem => problem.Code == "version")).IsTrue();
    }

    [Test]
    public async Task External_reference_and_forbidden_keyword_are_rejected_with_problems()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var http = ManagementApi.CreateClient(factory);
        await CreateChannelAsync(http, "bank-a");

        using var channel = ManagementApi.CreateChannel(factory);
        var grpc = new ManagementService.ManagementServiceClient(channel);

        var external = await UploadAsync(grpc, "bank-a", OpenApiSamples.ExternalReference);
        var forbidden = await UploadAsync(grpc, "bank-a", OpenApiSamples.ForbiddenKeyword);

        await Assert.That(external.Succeeded).IsFalse();
        await Assert.That(external.Problems.Any(problem => problem.Code == "ref")).IsTrue();
        await Assert.That(forbidden.Succeeded).IsFalse();
        await Assert.That(forbidden.Problems.Any(problem => problem.Code == "schema")).IsTrue();
    }

    [Test]
    public async Task Unknown_format_is_reported_as_warning()
    {
        using var factory = GatewayWebApplicationFactory.WithStorage(ManagementApi.Settings());
        using var http = ManagementApi.CreateClient(factory);
        await CreateChannelAsync(http, "bank-a");

        using var channel = ManagementApi.CreateChannel(factory);
        var grpc = new ManagementService.ManagementServiceClient(channel);

        var upload = await UploadAsync(grpc, "bank-a", OpenApiSamples.UnknownFormat);

        await Assert.That(upload.Succeeded).IsTrue();
        await Assert.That(upload.Problems.Any(problem =>
            problem.Severity == ProblemSeverity.Warning &&
            problem.Message.Contains("bank-account", StringComparison.Ordinal))).IsTrue();
    }
}
