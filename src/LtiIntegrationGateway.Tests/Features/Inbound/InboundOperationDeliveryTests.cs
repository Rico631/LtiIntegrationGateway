using LtiIntegrationGateway.Features.Contracts;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Contracts;
using LtiIntegrationGateway.Infrastructure.Routing;
using LtiIntegrationGateway.Tests.TestSupport;
using Microsoft.AspNetCore.Http;

namespace LtiIntegrationGateway.Tests.Features.Inbound;

/// <summary>
/// Доставка входящего вызова по действующим настройкам операции: очередь операции переопределяет
/// очередь версии (ADR §7.3, spec `inbound-routing`).
/// </summary>
public class InboundOperationDeliveryTests
{
    private const string VersionQueue = "version.queue";

    private static ContractOperation Operation(string? queue) => new()
    {
        Id = Guid.NewGuid(),
        ContractVersionId = InboundFixture.ContractVersionId,
        HttpMethod = "POST",
        Template = "/callbacks/contracts",
        OperationId = "receiveContract",
        RecipientQueue = queue,
    };

    private static ContractVersionSettings Version() => new(
        VersionQueue,
        ChannelAuthType.None,
        ConfigKey: null,
        HeaderName: null,
        UserName: null,
        RateLimitPermit: null,
        RateLimitWindowSeconds: null);

    /// <summary>
    /// Собирает метаданные endpoint'а так же, как это делает InboundEndpointDataSource: из набора
    /// маршрутов, построенного по действующим настройкам операции.
    /// </summary>
    private static InboundRouteMetadata MetadataOf(ContractOperation operation)
    {
        var set = ContractRouteSets.Build(
            InboundFixture.ContractVersionId,
            InboundFixture.ChannelName,
            [operation],
            Version(),
            new InboundRateLimitOptions());

        var route = set.Routes.Single();

        return new InboundRouteMetadata(
            route.ChannelName,
            route.ContractVersionId,
            route.OperationId,
            route.RecipientQueue,
            route.AuthType,
            route.ConfigKey,
            route.HeaderName,
            route.UserName,
            route.RateLimitPermit,
            route.RateLimitWindowSeconds);
    }

    [Test]
    public async Task Operation_queue_override_is_used_for_delivery_instead_of_version_queue()
    {
        await using var fixture = new InboundFixture();

        // Очередь операции — та, которую слушает продукт; очередь версии в тестовой сети не существует.
        var metadata = MetadataOf(Operation(InboundFixture.RecipientQueue));

        await Assert.That(metadata.RecipientQueue).IsEqualTo(InboundFixture.RecipientQueue);

        var status = await fixture.InvokeAsync(metadata);

        var messages = await fixture.Received.WaitForCountAsync(1, TimeSpan.FromSeconds(10));

        await Assert.That(status).IsEqualTo(StatusCodes.Status202Accepted);
        await Assert.That(messages.Count).IsEqualTo(1);
        await Assert.That(messages[0].ChannelName).IsEqualTo(InboundFixture.ChannelName);
        await Assert.That(messages[0].OperationId).IsEqualTo("receiveContract");
    }
}
