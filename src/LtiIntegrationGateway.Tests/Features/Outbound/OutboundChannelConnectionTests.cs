using System.Net;
using System.Security.Cryptography;
using LtiIntegrationGateway.Contracts.Messages.V1;
using LtiIntegrationGateway.Features.Channels;
using LtiIntegrationGateway.Features.Outbound;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities;
using LtiIntegrationGateway.Tests.TestSupport;

namespace LtiIntegrationGateway.Tests.Features.Outbound;

public class OutboundChannelConnectionTests
{
    private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(5);

    private static ProcessOutboundRequestCommand Command(OutboundRequest? request = null) =>
        new(request ?? OutboundFixture.CreateRequest(),
            OutboundFixture.ProductInbox,
            OutboundFixture.ProductInbox);

    [Test]
    public async Task Redirect_response_is_returned_instead_of_followed()
    {
        await using var fixture = new OutboundFixture();
        await fixture.CreateActiveChannelAsync();
        await fixture.RegisterProductAsync();

        fixture.Http.EnqueueResponse(
            HttpStatusCode.Found,
            "{}",
            location: "https://bank-a.example/api/other");

        using var db = fixture.CreateDb();
        await fixture.CreateProcessor(db).HandleAsync(Command());

        await Assert.That(fixture.Http.CallCount).IsEqualTo(1);

        var delivered = await fixture.Results.WaitForSingleAsync(DeliveryTimeout);

        await Assert.That(delivered.Outcome).IsEqualTo(Outcome.HttpResponse);
        await Assert.That(delivered.HttpStatusCode).IsEqualTo(302);
        await Assert.That(fixture.Http.Requests[0].Uri).IsEqualTo("https://bank-a.example/api/contracts");
    }

    [Test]
    public async Task Repeated_calls_of_the_same_channel_use_one_key()
    {
        await using var fixture = new OutboundFixture();
        using var certificate = TestCertificates.CreateSelfSigned("CN=lti-client-repeat");

        fixture.Certificates.With(
            "Channels:BankA:ClientCertificate",
            Convert.ToBase64String(TestCertificates.ExportPfx(certificate, TestCertificates.Password)),
            TestCertificates.Password);

        await fixture.CreateActiveChannelAsync(
            authType: ChannelAuthType.ClientCertificate,
            configKey: "Channels:BankA:ClientCertificate");
        await fixture.RegisterProductAsync();

        fixture.Http.EnqueueResponse(HttpStatusCode.OK);
        fixture.Http.EnqueueResponse(HttpStatusCode.OK);

        using (var db = fixture.CreateDb())
        {
            await fixture.CreateProcessor(db).HandleAsync(
                Command(OutboundFixture.CreateRequest("msg-1")));
        }

        using (var db = fixture.CreateDb())
        {
            await fixture.CreateProcessor(db).HandleAsync(
                Command(OutboundFixture.CreateRequest("msg-2")));
        }

        var acquired = fixture.ChannelClients.Acquired;

        await Assert.That(acquired.Count).IsEqualTo(2);
        await Assert.That(acquired[0].Key).IsEqualTo(acquired[1].Key);

        // Материал сертификата кэширован: PFX разбирается один раз, оба вызова видят тот же экземпляр.
        await Assert.That(ReferenceEquals(acquired[0].Certificate, acquired[1].Certificate)).IsTrue();
    }

    [Test]
    public async Task Channels_with_different_certificates_are_isolated_by_key()
    {
        await using var fixture = new OutboundFixture();
        using var certificateA = TestCertificates.CreateSelfSigned("CN=bank-a-client");
        using var certificateB = TestCertificates.CreateSelfSigned("CN=bank-b-client");

        fixture.Certificates.With(
            "Channels:BankA:ClientCertificate",
            Convert.ToBase64String(TestCertificates.ExportPfx(certificateA, TestCertificates.Password)),
            TestCertificates.Password);
        fixture.Certificates.With(
            "Channels:BankB:ClientCertificate",
            Convert.ToBase64String(TestCertificates.ExportPfx(certificateB, TestCertificates.Password)),
            TestCertificates.Password);

        await fixture.CreateActiveChannelAsync(
            authType: ChannelAuthType.ClientCertificate,
            configKey: "Channels:BankA:ClientCertificate");
        await fixture.CreateActiveChannelAsync(
            authType: ChannelAuthType.ClientCertificate,
            configKey: "Channels:BankB:ClientCertificate",
            channelName: "bank-b",
            baseUrl: "https://bank-b.example");
        await fixture.RegisterProductAsync([OutboundFixture.ChannelName, "bank-b"]);

        fixture.Http.EnqueueResponse(HttpStatusCode.OK);
        fixture.Http.EnqueueResponse(HttpStatusCode.OK);

        using (var db = fixture.CreateDb())
        {
            await fixture.CreateProcessor(db).HandleAsync(
                Command(OutboundFixture.CreateRequest("msg-a")));
        }

        using (var db = fixture.CreateDb())
        {
            await fixture.CreateProcessor(db).HandleAsync(
                Command(OutboundFixture.CreateRequest("msg-b", channelName: "bank-b")));
        }

        await fixture.Results.WaitForCountAsync(2, DeliveryTimeout);

        var acquired = fixture.ChannelClients.Acquired;

        await Assert.That(acquired.Count).IsEqualTo(2);
        await Assert.That(acquired[0].Key).IsNotEqualTo(acquired[1].Key);
        await Assert.That(acquired[0].Key.CertificateThumbprint)
            .IsEqualTo(certificateA.GetCertHashString(HashAlgorithmName.SHA256));
        await Assert.That(acquired[1].Key.CertificateThumbprint)
            .IsEqualTo(certificateB.GetCertHashString(HashAlgorithmName.SHA256));
        await Assert.That(acquired[0].Certificate!.Thumbprint).IsEqualTo(certificateA.Thumbprint);
        await Assert.That(acquired[1].Certificate!.Thumbprint).IsEqualTo(certificateB.Thumbprint);
    }

    [Test]
    public async Task Replacing_revision_with_another_certificate_switches_handler()
    {
        await using var fixture = new OutboundFixture();
        using var certificateA = TestCertificates.CreateSelfSigned("CN=bank-a-client-v1");
        using var certificateB = TestCertificates.CreateSelfSigned("CN=bank-a-client-v2");

        fixture.Certificates
            .With(
                "Channels:BankA:ClientCertificateV1",
                Convert.ToBase64String(TestCertificates.ExportPfx(certificateA, TestCertificates.Password)),
                TestCertificates.Password)
            .With(
                "Channels:BankA:ClientCertificateV2",
                Convert.ToBase64String(TestCertificates.ExportPfx(certificateB, TestCertificates.Password)),
                TestCertificates.Password);

        var channelId = await fixture.CreateActiveChannelAsync(
            authType: ChannelAuthType.ClientCertificate,
            configKey: "Channels:BankA:ClientCertificateV1");
        await fixture.RegisterProductAsync();

        fixture.Http.EnqueueResponse(HttpStatusCode.OK);

        using (var db = fixture.CreateDb())
        {
            await fixture.CreateProcessor(db).HandleAsync(
                Command(OutboundFixture.CreateRequest("msg-v1")));
        }

        var configuration = new ChannelConfiguration(
            "https://bank-a.example",
            ChannelAuthType.ClientCertificate,
            "Channels:BankA:ClientCertificateV2",
            HeaderName: null,
            UserName: null,
            PerAttemptTimeoutMs: 5000,
            OverallCapMs: 30000,
            new RetryPolicy([RetryRule.TransportError([1])]),
            AllowedMethods: ["GET", "POST"],
            AllowedPaths: ["/api/contracts", "/api/contracts/{id}"]);

        using (var db = fixture.CreateDb())
        {
            var draft = await new UpdateChannelDraftHandler(db)
                .HandleAsync(new UpdateChannelDraftCommand(channelId, null, configuration));

            await new PublishChannelRevisionHandler(db, new ChannelConfigurationValidator())
                .HandleAsync(new PublishChannelRevisionCommand(channelId, draft.RevisionNumber));

            await new ActivateChannelRevisionHandler(db)
                .HandleAsync(new ActivateChannelRevisionCommand(channelId, draft.RevisionNumber!.Value));
        }

        fixture.Http.EnqueueResponse(HttpStatusCode.OK);

        using (var db = fixture.CreateDb())
        {
            await fixture.CreateProcessor(db).HandleAsync(
                Command(OutboundFixture.CreateRequest("msg-v2")));
        }

        var acquired = fixture.ChannelClients.Acquired;

        await Assert.That(acquired.Count).IsEqualTo(2);
        await Assert.That(acquired[0].Key).IsNotEqualTo(acquired[1].Key);
        await Assert.That(acquired[1].Key.CertificateThumbprint)
            .IsEqualTo(certificateB.GetCertHashString(HashAlgorithmName.SHA256));
        await Assert.That(acquired[0].Certificate!.Thumbprint).IsEqualTo(certificateA.Thumbprint);
        await Assert.That(acquired[1].Certificate!.Thumbprint).IsEqualTo(certificateB.Thumbprint);
    }
}
