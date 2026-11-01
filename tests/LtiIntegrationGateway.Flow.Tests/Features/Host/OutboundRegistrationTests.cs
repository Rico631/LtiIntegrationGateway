using LtiIntegrationGateway.Contracts.Messages.V1;
using LtiIntegrationGateway.Features.Outbound;
using LtiIntegrationGateway.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Rebus.Handlers;

namespace LtiIntegrationGateway.Tests.Features.Host;

public class OutboundRegistrationTests
{
    [Test]
    public async Task Outbound_handler_is_registered_for_the_bus()
    {
        using var factory = FlowHostFactory.WithStorage();

        var handlers = factory.Services.GetServices<IHandleMessages<OutboundRequest>>().ToArray();

        await Assert.That(handlers.Length).IsEqualTo(1);
        await Assert.That(handlers[0]).IsAssignableTo<OutboundRequestHandler>();
    }

    [Test]
    public async Task Outbound_steps_are_resolvable_from_the_container()
    {
        using var factory = FlowHostFactory.WithStorage();
        using var scope = factory.Services.CreateScope();

        var processor = scope.ServiceProvider.GetService<ProcessOutboundRequestHandler>();

        await Assert.That(processor).IsNotNull();
        await Assert.That(scope.ServiceProvider.GetService<LtiIntegrationGateway.Infrastructure.Security.ISecretStore>()).IsNotNull();
    }
}