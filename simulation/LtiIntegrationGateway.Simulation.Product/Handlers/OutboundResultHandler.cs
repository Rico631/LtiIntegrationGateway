using LtiIntegrationGateway.Contracts.Messages.V1;
using Rebus.Handlers;

namespace LtiIntegrationGateway.Simulation.Product.Handlers;

/// <summary>Пишет в лог ответ внешней системы на исходящий вызов.</summary>
public sealed class OutboundResultHandler(ILogger<OutboundResultHandler> logger) : IHandleMessages<OutboundResult>
{
    public Task Handle(OutboundResult message)
    {
        ArgumentNullException.ThrowIfNull(message);

        logger.LogInformation(
            "Ответ на POST /customer: messageId={MessageId}, outcome={Outcome}, status={Status}, attempts={Attempts}, body={Body}",
            message.MessageId,
            message.Outcome,
            message.HttpStatusCode,
            message.AttemptCount,
            message.ResponseBody.ToStringUtf8());

        return Task.CompletedTask;
    }
}

