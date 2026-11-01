using System.Net;
using System.Text.Json;
using LtiIntegrationGateway.Contracts.Management.V1;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace LtiIntegrationGateway.Tests.Features.Management;

public class ListIntegrationsTests
{
    private static StringContent CreateChannelBody(string name) =>
        ManagementApi.Json(JsonSerializer.Serialize(new
        {
            name,
            settings = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(new
            {
                baseUrl = "https://bank-a.example",
                authType = "none",
                perAttemptTimeoutMs = 5000,
                overallCapMs = 30000,
                retryPolicy = new[]
                {
                    new { match = "transport_error", statusCodes = Array.Empty<int>(), delaysMs = new[] { 100 } },
                },
            })),
        }));

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.Clone();
    }

    private static string[] Integrations(JsonElement json) =>
        [.. json.GetProperty("integrations").EnumerateArray()
            .Select(item => item.GetProperty("integration").GetString()!)];

    [Test]
    public async Task Empty_storage_returns_empty_list()
    {
        using var factory = ManagementHostFactory.WithStorage(ManagementApi.Settings());
        using var client = ManagementApi.CreateClient(factory);

        var response = await client.GetAsync("/v1/integrations");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var json = await ReadJsonAsync(response);

        await Assert.That(json.GetProperty("integrations").GetArrayLength()).IsEqualTo(0);
    }

    [Test]
    public async Task Configured_integrations_are_listed_in_identifier_order()
    {
        using var factory = ManagementHostFactory.WithStorage(ManagementApi.Settings());
        using var client = ManagementApi.CreateClient(factory);

        foreach (var integration in new[] { "bank-b", "bank-a", "default" })
        {
            var created = await client.PostAsync(
                $"/v1/integrations/{integration}/outbound-channels",
                CreateChannelBody("payments"));

            await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }

        var json = await ReadJsonAsync(await client.GetAsync("/v1/integrations"));

        await Assert.That(string.Join(",", Integrations(json))).IsEqualTo("bank-a,bank-b,default");

        foreach (var item in json.GetProperty("integrations").EnumerateArray())
        {
            await Assert.That(item.GetProperty("configurationVersion").GetInt64()).IsEqualTo(1L);
            await Assert.That(item.GetProperty("createdAt").GetString()!.Length).IsGreaterThan(0);
        }
    }

    [Test]
    public async Task Rest_and_grpc_return_same_integrations()
    {
        using var factory = ManagementHostFactory.WithStorage(ManagementApi.Settings());
        using var client = ManagementApi.CreateClient(factory);

        await client.PostAsync("/v1/integrations/bank-a/outbound-channels", CreateChannelBody("payments"));

        var rest = Integrations(await ReadJsonAsync(await client.GetAsync("/v1/integrations")));

        using var channel = ManagementApi.CreateChannel(factory);
        var grpc = new ManagementService.ManagementServiceClient(channel);
        var response = await grpc.ListIntegrationsAsync(new ListIntegrationsRequest(), ManagementApi.Call());

        await Assert.That(string.Join(",", rest))
            .IsEqualTo(string.Join(",", response.Integrations.Select(item => item.Integration)));

        using var anonymous = ManagementApi.CreateClient(factory, apiKey: null);
        var unauthorized = await anonymous.GetAsync("/v1/integrations");

        await Assert.That(unauthorized.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task List_does_not_write_audit_entries()
    {
        using var factory = ManagementHostFactory.WithStorage(ManagementApi.Settings());
        using var client = ManagementApi.CreateClient(factory);

        await client.PostAsync("/v1/integrations/bank-a/outbound-channels", CreateChannelBody("payments"));

        var before = CountAuditEntries(factory);

        await client.GetAsync("/v1/integrations");

        await Assert.That(CountAuditEntries(factory)).IsEqualTo(before);
    }

    private static int CountAuditEntries(ManagementHostFactory factory)
    {
        using var scope = factory.Services.CreateScope();

        return scope.ServiceProvider.GetRequiredService<GatewayDbContext>().ManagementAuditEntries.Count();
    }
}
