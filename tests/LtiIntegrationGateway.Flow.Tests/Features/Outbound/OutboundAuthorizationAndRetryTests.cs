using System.Net;
using System.Security.Cryptography;
using System.Text;
using LtiIntegrationGateway.Contracts.Messages.V1;
using LtiIntegrationGateway.Features.Outbound;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;
using LtiIntegrationGateway.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace LtiIntegrationGateway.Tests.Features.Outbound;

public class OutboundAuthorizationAndRetryTests
{
    private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(5);

    private static ProcessOutboundRequestCommand Command(OutboundRequest? request = null) =>
        new(request ?? OutboundFixture.CreateRequest(),
            OutboundFixture.ProductInbox,
            OutboundFixture.ProductInbox);

    [Test]
    public async Task Bearer_token_is_attached_from_secret_store()
    {
        await using var fixture = new OutboundFixture();
        fixture.Secrets.With("Secrets:BankA:Token", "token-123");
        await fixture.CreateActiveChannelAsync(authType: ChannelAuthType.Bearer, configKey: "Secrets:BankA:Token");
        await fixture.RegisterProductAsync();

        fixture.Http.EnqueueResponse(HttpStatusCode.OK);

        using var db = fixture.CreateDb();
        await fixture.CreateProcessor(db).HandleAsync(Command());

        await Assert.That(fixture.Http.CallCount).IsEqualTo(1);
        await Assert.That(fixture.Http.Requests[0].Headers["Authorization"]).IsEqualTo("Bearer token-123");
    }

    [Test]
    public async Task Api_key_is_attached_as_header()
    {
        await using var fixture = new OutboundFixture();
        fixture.Secrets.With("Secrets:BankA:ApiKey", "key-9");
        await fixture.CreateActiveChannelAsync(authType: ChannelAuthType.ApiKey, configKey: "Secrets:BankA:ApiKey");
        await fixture.RegisterProductAsync();

        fixture.Http.EnqueueResponse(HttpStatusCode.OK);

        using var db = fixture.CreateDb();
        await fixture.CreateProcessor(db).HandleAsync(Command());

        await Assert.That(fixture.Http.Requests[0].Headers["X-Api-Key"]).IsEqualTo("key-9");
    }

    [Test]
    public async Task Missing_secret_is_refused_without_http_call()
    {
        await using var fixture = new OutboundFixture();
        await fixture.CreateActiveChannelAsync(authType: ChannelAuthType.Bearer, configKey: "Secrets:BankA:Token");
        await fixture.RegisterProductAsync();

        using var db = fixture.CreateDb();
        await fixture.CreateProcessor(db).HandleAsync(Command());

        await Assert.That(fixture.Http.CallCount).IsEqualTo(0);

        var delivered = await fixture.Results.WaitForSingleAsync(DeliveryTimeout);

        await Assert.That(delivered.ErrorCode).IsEqualTo(OutboundErrorCodes.SecretNotConfigured);
        await Assert.That(delivered.ErrorMessage.Contains("Secrets:BankA:Token", StringComparison.Ordinal)).IsTrue();
        await Assert.That(delivered.ErrorMessage.StartsWith("secret key missing (key:", StringComparison.Ordinal))
            .IsTrue();
    }

    [Test]
    public async Task Unknown_auth_type_is_refused_as_unsupported()
    {
        await using var fixture = new OutboundFixture();
        await fixture.CreateActiveChannelAsync(
            authType: (ChannelAuthType)99,
            configKey: "Secrets:BankA:Unknown");
        await fixture.RegisterProductAsync();

        using var db = fixture.CreateDb();
        await fixture.CreateProcessor(db).HandleAsync(Command());

        await Assert.That(fixture.Http.CallCount).IsEqualTo(0);

        var delivered = await fixture.Results.WaitForSingleAsync(DeliveryTimeout);

        await Assert.That(delivered.Outcome).IsEqualTo(Outcome.GatewayRefused);
        await Assert.That(delivered.ErrorCode).IsEqualTo(OutboundErrorCodes.AuthTypeUnsupported);
    }

    [Test]
    public async Task Client_certificate_without_material_is_refused_before_http_call()
    {
        await using var fixture = new OutboundFixture();
        await fixture.CreateActiveChannelAsync(
            authType: ChannelAuthType.ClientCertificate,
            configKey: "Channels:BankA:ClientCertificate");
        await fixture.RegisterProductAsync();

        using var db = fixture.CreateDb();
        await fixture.CreateProcessor(db).HandleAsync(Command());

        await Assert.That(fixture.Http.CallCount).IsEqualTo(0);
        await Assert.That(fixture.ChannelClients.Acquired.Count).IsEqualTo(0);

        var delivered = await fixture.Results.WaitForSingleAsync(DeliveryTimeout);

        await Assert.That(delivered.Outcome).IsEqualTo(Outcome.GatewayRefused);
        await Assert.That(delivered.ErrorCode).IsEqualTo(OutboundErrorCodes.SecretNotConfigured);
        await Assert.That(delivered.ErrorMessage.Contains("Channels:BankA:ClientCertificate", StringComparison.Ordinal))
            .IsTrue();
    }

    [Test]
    public async Task Client_certificate_is_presented_to_the_handler_without_leaking_material()
    {
        await using var fixture = new OutboundFixture();
        using var certificate = TestCertificates.CreateSelfSigned("CN=lti-client");
        var material = Convert.ToBase64String(
            TestCertificates.ExportPfx(certificate, TestCertificates.Password));

        fixture.Certificates.With("Channels:BankA:ClientCertificate", material, TestCertificates.Password);

        await fixture.CreateActiveChannelAsync(
            authType: ChannelAuthType.ClientCertificate,
            configKey: "Channels:BankA:ClientCertificate");
        await fixture.RegisterProductAsync();

        fixture.Http.EnqueueResponse(HttpStatusCode.OK);

        using var db = fixture.CreateDb();
        var processed = await fixture.CreateProcessor(db).HandleAsync(Command());

        await Assert.That(processed.Completed).IsTrue();
        await Assert.That(fixture.Http.CallCount).IsEqualTo(1);

        var acquired = fixture.ChannelClients.Acquired.Single();
        var expectedThumbprint = certificate.GetCertHashString(HashAlgorithmName.SHA256);

        await Assert.That(acquired.Key.AuthType).IsEqualTo(ChannelAuthType.ClientCertificate);
        await Assert.That(acquired.Key.BaseUrl).IsEqualTo("https://bank-a.example");
        await Assert.That(acquired.Key.CertificateThumbprint).IsEqualTo(expectedThumbprint);
        await Assert.That(acquired.Certificate).IsNotNull();
        await Assert.That(acquired.Certificate!.GetCertHashString(HashAlgorithmName.SHA256))
            .IsEqualTo(expectedThumbprint);

        var delivered = await fixture.Results.WaitForSingleAsync(DeliveryTimeout);
        var resultJson = ContractJson.Serialize(delivered);

        await Assert.That(resultJson.Contains(material, StringComparison.Ordinal)).IsFalse();
        await Assert.That(resultJson.Contains(TestCertificates.Password, StringComparison.Ordinal)).IsFalse();

        using var verifyDb = fixture.CreateDb();
        var log = await verifyDb.OutboundMessageLogs.SingleAsync();

        await Assert.That(Encoding.UTF8.GetString(log.RequestBody).Contains(material, StringComparison.Ordinal))
            .IsFalse();
        await Assert.That(Encoding.UTF8.GetString(log.ResponseBody).Contains(material, StringComparison.Ordinal))
            .IsFalse();
        await Assert.That(log.ErrorCode).IsNull();
    }

    [Test]
    public async Task Unusable_client_certificate_material_is_refused_without_leaking_values()
    {
        await using var fixture = new OutboundFixture();
        using var certificate = TestCertificates.CreateSelfSigned("CN=lti-client-broken");
        var material = Convert.ToBase64String(
            TestCertificates.ExportPfx(certificate, TestCertificates.Password));
        const string wrongPassword = "wrong-pfx-password-2";

        fixture.Certificates.With("Channels:BankA:ClientCertificate", material, wrongPassword);

        await fixture.CreateActiveChannelAsync(
            authType: ChannelAuthType.ClientCertificate,
            configKey: "Channels:BankA:ClientCertificate");
        await fixture.RegisterProductAsync();

        using var db = fixture.CreateDb();
        await fixture.CreateProcessor(db).HandleAsync(Command());

        await Assert.That(fixture.Http.CallCount).IsEqualTo(0);
        await Assert.That(fixture.ChannelClients.Acquired.Count).IsEqualTo(0);

        var delivered = await fixture.Results.WaitForSingleAsync(DeliveryTimeout);

        await Assert.That(delivered.Outcome).IsEqualTo(Outcome.GatewayRefused);
        await Assert.That(delivered.ErrorCode).IsEqualTo(OutboundErrorCodes.SecretNotConfigured);
        await Assert.That(delivered.ErrorMessage.Contains("Channels:BankA:ClientCertificate", StringComparison.Ordinal))
            .IsTrue();
        await Assert.That(delivered.ErrorMessage.Contains(TestCertificates.Password, StringComparison.Ordinal))
            .IsFalse();
        await Assert.That(delivered.ErrorMessage.Contains(wrongPassword, StringComparison.Ordinal)).IsFalse();
        await Assert.That(delivered.ErrorMessage.Contains(material[..32], StringComparison.Ordinal)).IsFalse();

        using var verifyDb = fixture.CreateDb();
        var log = await verifyDb.OutboundMessageLogs.SingleAsync();

        await Assert.That(log.ErrorCode).IsEqualTo(OutboundErrorCodes.SecretNotConfigured);
        await Assert.That(Encoding.UTF8.GetString(log.RequestBody).Contains(material, StringComparison.Ordinal))
            .IsFalse();
    }

    [Test]
    public async Task Retryable_status_is_repeated_until_success()
    {
        await using var fixture = new OutboundFixture();
        await fixture.CreateActiveChannelAsync(
            retryPolicy: new RetryPolicy([RetryRule.HttpStatusIn([429, 503], [1])]));
        await fixture.RegisterProductAsync();

        fixture.Http.EnqueueResponse(HttpStatusCode.TooManyRequests);
        fixture.Http.EnqueueResponse(HttpStatusCode.OK, "{\"ok\":true}");

        using var db = fixture.CreateDb();
        await fixture.CreateProcessor(db).HandleAsync(Command());

        await Assert.That(fixture.Http.CallCount).IsEqualTo(2);

        var delivered = await fixture.Results.WaitForSingleAsync(DeliveryTimeout);

        await Assert.That(delivered.HttpStatusCode).IsEqualTo(200);
        await Assert.That(delivered.AttemptCount).IsEqualTo(2);
    }

    [Test]
    public async Task Api_key_is_sent_as_query_parameter_when_header_not_configured()
    {
        await using var fixture = new OutboundFixture();
        fixture.Secrets.With("Secrets:BankA:ApiKey", "key-9");
        await fixture.CreateActiveChannelAsync(
            authType: ChannelAuthType.ApiKey,
            configKey: "Secrets:BankA:ApiKey",
            headerName: null);
        await fixture.RegisterProductAsync();

        fixture.Http.EnqueueResponse(HttpStatusCode.OK);

        using var db = fixture.CreateDb();
        await fixture.CreateProcessor(db).HandleAsync(Command());

        await Assert.That(fixture.Http.Requests[0].Uri)
            .IsEqualTo("https://bank-a.example/api/contracts?api_key=key-9");
    }

    [Test]
    public async Task Upstream_error_status_and_body_are_passed_through()
    {
        await using var fixture = new OutboundFixture();
        await fixture.CreateActiveChannelAsync();
        await fixture.RegisterProductAsync();

        fixture.Http.EnqueueResponse(HttpStatusCode.InternalServerError, "{\"error\":\"boom\"}");

        using var db = fixture.CreateDb();
        await fixture.CreateProcessor(db).HandleAsync(Command());

        await Assert.That(fixture.Http.CallCount).IsEqualTo(1);

        var delivered = await fixture.Results.WaitForSingleAsync(DeliveryTimeout);

        await Assert.That(delivered.Outcome).IsEqualTo(Outcome.HttpResponse);
        await Assert.That(delivered.HttpStatusCode).IsEqualTo(500);
        await Assert.That(delivered.ResponseBody.ToStringUtf8()).IsEqualTo("{\"error\":\"boom\"}");
    }

    [Test]
    public async Task Post_without_idempotency_key_is_not_retried()
    {
        await using var fixture = new OutboundFixture();
        await fixture.CreateActiveChannelAsync(
            retryPolicy: new RetryPolicy([RetryRule.HttpStatusIn([503], [1])]));
        await fixture.RegisterProductAsync();

        fixture.Http.EnqueueResponse(HttpStatusCode.ServiceUnavailable);

        using var db = fixture.CreateDb();
        await fixture.CreateProcessor(db).HandleAsync(Command(
            OutboundFixture.CreateRequest(method: "POST")));

        await Assert.That(fixture.Http.CallCount).IsEqualTo(1);

        var delivered = await fixture.Results.WaitForSingleAsync(DeliveryTimeout);

        await Assert.That(delivered.HttpStatusCode).IsEqualTo(503);
    }

    [Test]
    public async Task Post_with_idempotency_key_is_retried()
    {
        await using var fixture = new OutboundFixture();
        await fixture.CreateActiveChannelAsync(
            retryPolicy: new RetryPolicy([RetryRule.HttpStatusIn([503], [1])]));
        await fixture.RegisterProductAsync();

        fixture.Http.EnqueueResponse(HttpStatusCode.ServiceUnavailable);
        fixture.Http.EnqueueResponse(HttpStatusCode.Accepted);

        using var db = fixture.CreateDb();
        await fixture.CreateProcessor(db).HandleAsync(Command(
            OutboundFixture.CreateRequest(method: "POST", idempotencyKey: "idem-7")));

        await Assert.That(fixture.Http.CallCount).IsEqualTo(2);
    }

    [Test]
    public async Task Exhausted_transport_retries_produce_transport_error()
    {
        await using var fixture = new OutboundFixture();
        await fixture.CreateActiveChannelAsync(
            retryPolicy: new RetryPolicy([RetryRule.TransportError([1])]));
        await fixture.RegisterProductAsync();

        fixture.Http.EnqueueException(new HttpRequestException("boom"));
        fixture.Http.EnqueueException(new HttpRequestException("boom"));

        using var db = fixture.CreateDb();
        await fixture.CreateProcessor(db).HandleAsync(Command());

        await Assert.That(fixture.Http.CallCount).IsEqualTo(2);

        var delivered = await fixture.Results.WaitForSingleAsync(DeliveryTimeout);

        await Assert.That(delivered.Outcome).IsEqualTo(Outcome.TransportError);
        await Assert.That(delivered.ErrorCode).IsEqualTo(OutboundErrorCodes.RetriesExhausted);
        await Assert.That(delivered.AttemptCount).IsEqualTo(2);
    }

    [Test]
    public async Task Retry_after_beyond_overall_cap_stops_retries()
    {
        await using var fixture = new OutboundFixture();
        await fixture.CreateActiveChannelAsync(
            retryPolicy: new RetryPolicy([RetryRule.HttpStatusIn([429], [1])]),
            perAttemptTimeoutMs: 200,
            overallCapMs: 300);
        await fixture.RegisterProductAsync();

        fixture.Http.EnqueueResponseWithRetryAfter(HttpStatusCode.TooManyRequests, TimeSpan.FromSeconds(5));

        using var db = fixture.CreateDb();
        await fixture.CreateProcessor(db).HandleAsync(Command());

        await Assert.That(fixture.Http.CallCount).IsEqualTo(1);

        var delivered = await fixture.Results.WaitForSingleAsync(DeliveryTimeout);

        await Assert.That(delivered.HttpStatusCode).IsEqualTo(429);
    }

    [Test]
    public async Task Tls_handshake_failure_is_classified_as_tls_failed()
    {
        await using var fixture = new OutboundFixture();
        await fixture.CreateActiveChannelAsync(
            retryPolicy: new RetryPolicy([RetryRule.HttpStatusIn([503], [1])]));
        await fixture.RegisterProductAsync();

        fixture.Http.EnqueueException(new HttpRequestException(
            "не удалось установить защищённое соединение",
            new System.Security.Authentication.AuthenticationException("TLS handshake failed")));

        using var db = fixture.CreateDb();
        await fixture.CreateProcessor(db).HandleAsync(Command());

        await Assert.That(fixture.Http.CallCount).IsEqualTo(1);

        var delivered = await fixture.Results.WaitForSingleAsync(DeliveryTimeout);

        await Assert.That(delivered.Outcome).IsEqualTo(Outcome.TransportError);
        await Assert.That(delivered.ErrorCode).IsEqualTo(OutboundErrorCodes.TlsFailed);
        await Assert.That(delivered.AttemptCount).IsEqualTo(1);
    }
}
