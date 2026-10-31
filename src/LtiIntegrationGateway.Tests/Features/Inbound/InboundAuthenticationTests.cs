using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities;
using LtiIntegrationGateway.Tests.TestSupport;
using Microsoft.AspNetCore.Http;

namespace LtiIntegrationGateway.Tests.Features.Inbound;

public class InboundAuthenticationTests
{
    private const string Body = """{ "contractNumber": "DKP-1" }""";

    private const string Schemas = """
    {
      "application/json": {
        "type": "object",
        "required": ["contractNumber"],
        "properties": { "contractNumber": { "type": "string" } }
      }
    }
    """;

    private static X509Certificate2 ClientCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=bank-a-client", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1));
    }

    [Test]
    public async Task Api_key_call_with_correct_header_is_accepted()
    {
        await using var fixture = new InboundFixture();
        fixture.Secrets.With("bank-a.inbound", "s3cret");
        await fixture.WarmUpAsync("POST", "/callbacks/contracts", Schemas);

        var status = await fixture.InvokeAsync(
            fixture.Route(ChannelAuthType.ApiKey, "bank-a.inbound", headerName: "X-Bank-Key"),
            body: Body,
            headers: new Dictionary<string, string> { ["X-Bank-Key"] = "s3cret" });

        await Assert.That(status).IsEqualTo(StatusCodes.Status202Accepted);
    }

    [Test]
    public async Task Api_key_call_with_wrong_or_missing_header_is_rejected()
    {
        await using var fixture = new InboundFixture();
        fixture.Secrets.With("bank-a.inbound", "s3cret");
        await fixture.WarmUpAsync("POST", "/callbacks/contracts", Schemas);

        var route = fixture.Route(ChannelAuthType.ApiKey, "bank-a.inbound", headerName: "X-Bank-Key");

        var wrong = await fixture.InvokeAsync(
            route,
            body: Body,
            headers: new Dictionary<string, string> { ["X-Bank-Key"] = "wrong" });

        var missing = await fixture.InvokeAsync(route, body: Body);

        await Assert.That(wrong).IsEqualTo(StatusCodes.Status401Unauthorized);
        await Assert.That(missing).IsEqualTo(StatusCodes.Status401Unauthorized);
    }

    [Test]
    public async Task Api_key_without_configured_secret_returns_service_unavailable()
    {
        await using var fixture = new InboundFixture();
        await fixture.WarmUpAsync("POST", "/callbacks/contracts", Schemas);

        var status = await fixture.InvokeAsync(
            fixture.Route(ChannelAuthType.ApiKey, "bank-a.inbound", headerName: "X-Bank-Key"),
            body: Body,
            headers: new Dictionary<string, string> { ["X-Bank-Key"] = "anything" });

        await Assert.That(status).IsEqualTo(StatusCodes.Status503ServiceUnavailable);
    }

    [Test]
    public async Task Basic_call_with_correct_credentials_is_accepted()
    {
        await using var fixture = new InboundFixture();
        fixture.Secrets.With("bank-a.inbound.basic", "p@ssw0rd");
        await fixture.WarmUpAsync("POST", "/callbacks/contracts", Schemas);

        var credentials = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("bank-a:p@ssw0rd"));

        var status = await fixture.InvokeAsync(
            fixture.Route(ChannelAuthType.Basic, "bank-a.inbound.basic", userName: "bank-a"),
            body: Body,
            headers: new Dictionary<string, string> { ["Authorization"] = $"Basic {credentials}" });

        await Assert.That(status).IsEqualTo(StatusCodes.Status202Accepted);
    }

    [Test]
    public async Task Basic_call_with_wrong_password_is_rejected()
    {
        await using var fixture = new InboundFixture();
        fixture.Secrets.With("bank-a.inbound.basic", "p@ssw0rd");
        await fixture.WarmUpAsync("POST", "/callbacks/contracts", Schemas);

        var credentials = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("bank-a:wrong"));

        var status = await fixture.InvokeAsync(
            fixture.Route(ChannelAuthType.Basic, "bank-a.inbound.basic", userName: "bank-a"),
            body: Body,
            headers: new Dictionary<string, string> { ["Authorization"] = $"Basic {credentials}" });

        await Assert.That(status).IsEqualTo(StatusCodes.Status401Unauthorized);
    }

    [Test]
    public async Task Client_certificate_is_required_when_configured()
    {
        await using var fixture = new InboundFixture();
        await fixture.WarmUpAsync("POST", "/callbacks/contracts", Schemas);

        var route = fixture.Route(ChannelAuthType.ClientCertificate, "bank-a.inbound");

        var withoutCertificate = await fixture.InvokeAsync(route, body: Body);

        using var certificate = ClientCertificate();
        var withCertificate = await fixture.InvokeAsync(route, body: Body, clientCertificate: certificate);

        await Assert.That(withoutCertificate).IsEqualTo(StatusCodes.Status401Unauthorized);
        await Assert.That(withCertificate).IsEqualTo(StatusCodes.Status202Accepted);
    }

    [Test]
    public async Task Rejection_response_does_not_disclose_secret_or_reason()
    {
        await using var fixture = new InboundFixture();
        fixture.Secrets.With("bank-a.inbound", "s3cret-value");
        await fixture.WarmUpAsync("POST", "/callbacks/contracts", Schemas);

        var status = await fixture.InvokeAsync(
            fixture.Route(ChannelAuthType.ApiKey, "bank-a.inbound", headerName: "X-Bank-Key"),
            body: Body,
            headers: new Dictionary<string, string> { ["X-Bank-Key"] = "s3cret-value-wrong" });

        var logs = await fixture.LogsAsync();

        await Assert.That(status).IsEqualTo(StatusCodes.Status401Unauthorized);
        await Assert.That(fixture.Received.Messages.Count).IsEqualTo(0);
        await Assert.That(logs.Count).IsEqualTo(1);
        await Assert.That(logs[0].ErrorCode).IsEqualTo("auth: аутентификация не пройдена");
        await Assert.That(logs[0].QueryParameters?.Contains("s3cret", StringComparison.Ordinal) ?? false).IsFalse();
    }
}
