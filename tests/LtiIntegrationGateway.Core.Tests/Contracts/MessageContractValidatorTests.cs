using Google.Protobuf;
using LtiIntegrationGateway.Contracts;
using LtiIntegrationGateway.Contracts.Messages.V1;

namespace LtiIntegrationGateway.Tests.Contracts;

public class MessageContractValidatorTests
{
    private static OutboundRequest CreateValidRequest() => new()
    {
        MessageId = "d7a7e0c5-3f62-48e4-8a18-55d19e219cc0",
        ProductId = "dpv",
        ChannelName = "bank-a",
        RelativePath = "/api/contracts",
        HttpMethod = "POST",
        ContentType = "application/json",
        Body = ByteString.CopyFromUtf8("{\"contractNumber\":\"DKP-456\"}"),
    };

    [Test]
    public async Task Complete_request_has_no_violations()
    {
        var violations = MessageContractValidator.ValidateOutboundRequest(CreateValidRequest());

        await Assert.That(violations.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Missing_product_id_is_reported()
    {
        var request = CreateValidRequest();
        request.ProductId = string.Empty;

        var violations = MessageContractValidator.ValidateOutboundRequest(request);

        await Assert.That(violations.Contains("product_id")).IsTrue();
    }

    [Test]
    public async Task Body_without_content_type_is_reported()
    {
        var request = CreateValidRequest();
        request.ClearContentType();

        var violations = MessageContractValidator.ValidateOutboundRequest(request);

        await Assert.That(violations.Contains("content_type")).IsTrue();
    }
}
