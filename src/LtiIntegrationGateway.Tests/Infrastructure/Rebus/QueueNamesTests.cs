using LtiIntegrationGateway.Infrastructure.Rebus;

namespace LtiIntegrationGateway.Tests.Infrastructure.Rebus;

/// <summary>Имена очередей шины, зафиксированные ADR §10.2 (решение 28).</summary>
public class QueueNamesTests
{
    [Test]
    public async Task Queue_names_match_adr()
    {
        await Assert.That(QueueNames.OutboundQueue).IsEqualTo("lti.gateway.outbound");
        await Assert.That(QueueNames.ErrorQueue).IsEqualTo("lti.gateway.error");
        await Assert.That(QueueNames.ErrorQueue).IsNotEqualTo($"{QueueNames.OutboundQueue}.error");
    }

    [Test]
    public async Task Product_inbox_is_computed_from_product_id()
    {
        await Assert.That(QueueNames.ProductInbox("dpv")).IsEqualTo("dpv.inbox");
        await Assert.That(QueueNames.ProductInbox("bank-a")).IsEqualTo("bank-a.inbox");
    }

    [Test]
    public async Task Product_inbox_format_accepts_only_product_id_and_inbox_suffix()
    {
        await Assert.That(QueueNames.IsProductInbox("dpv.inbox")).IsTrue();
        await Assert.That(QueueNames.IsProductInbox("bank-a.inbox")).IsTrue();
        await Assert.That(QueueNames.IsProductInbox("inbound.queue")).IsFalse();
        await Assert.That(QueueNames.IsProductInbox("dpv.inbox.secondary")).IsFalse();
        await Assert.That(QueueNames.IsProductInbox("DPV.inbox")).IsFalse();
        await Assert.That(QueueNames.IsProductInbox(string.Empty)).IsFalse();
        await Assert.That(QueueNames.IsProductInbox(null)).IsFalse();
    }
}
