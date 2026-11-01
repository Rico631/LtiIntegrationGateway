using LtiIntegrationGateway.Infrastructure.Rebus;

namespace LtiIntegrationGateway.Tests.Infrastructure.Rebus;

/// <summary>Имена очередей шины вычисляются от интеграции, имя продукта — от <c>product_id</c>.</summary>
public class QueueNamesTests
{
    [Test]
    public async Task Gateway_queues_are_derived_from_integration()
    {
        await Assert.That(QueueNames.OutboundQueue("bank-a")).IsEqualTo("lti.gateway.bank-a.outbound");
        await Assert.That(QueueNames.ErrorQueue("bank-a")).IsEqualTo("lti.gateway.bank-a.error");
        await Assert.That(QueueNames.ErrorQueue("bank-b")).IsEqualTo("lti.gateway.bank-b.error");
        await Assert.That(QueueNames.ErrorQueue("bank-a"))
            .IsNotEqualTo($"{QueueNames.OutboundQueue("bank-a")}.error");
    }

    [Test]
    public async Task Replica_count_does_not_change_queue_address()
    {
        // Адрес зависит только от интеграции: число реплик меняет число конкурирующих потребителей.
        await Assert.That(QueueNames.OutboundQueue("bank-a")).IsEqualTo(QueueNames.OutboundQueue("bank-a"));
    }

    [Test]
    public async Task Invalid_integration_name_is_rejected()
    {
        foreach (var name in new[] { "Bank-A", "bank_a", "bank a", "-bank", "a", string.Empty })
        {
            var rejected = false;

            try
            {
                QueueNames.OutboundQueue(name);
            }
            catch (ArgumentException)
            {
                rejected = true;
            }

            await Assert.That(rejected).IsTrue();
            await Assert.That(QueueNames.IsIntegrationId(name)).IsFalse();
        }

        await Assert.That(QueueNames.IsIntegrationId("bank-a")).IsTrue();
        await Assert.That(QueueNames.IsIntegrationId(null)).IsFalse();
    }

    [Test]
    public async Task Outbound_queue_address_is_parsed_back_to_integration()
    {
        await Assert.That(QueueNames.TryParseOutboundQueue("lti.gateway.bank-a.outbound", out var integration)).IsTrue();
        await Assert.That(integration).IsEqualTo("bank-a");
        await Assert.That(QueueNames.TryParseOutboundQueue("lti.gateway.outbound", out _)).IsFalse();
        await Assert.That(QueueNames.TryParseOutboundQueue("lti.gateway.bank-a.error", out _)).IsFalse();
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
