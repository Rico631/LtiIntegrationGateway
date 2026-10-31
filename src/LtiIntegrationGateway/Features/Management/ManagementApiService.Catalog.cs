using Grpc.Core;
using LtiIntegrationGateway.Contracts;
using LtiIntegrationGateway.Contracts.Management.V1;
using LtiIntegrationGateway.Features.ProductRoutes.GetProductRoute;
using Microsoft.EntityFrameworkCore;

namespace LtiIntegrationGateway.Features.Management;

/// <summary>Маршруты продуктов и состояние исходящих операций.</summary>
public sealed partial class ManagementApiService
{
    public override async Task<OperationResponse> RegisterProductRoute(
        RegisterProductRouteRequest request,
        ServerCallContext context)
    {
        var problems = MessageContractValidator.ValidateRegisterProductRoute(
            request.ProductId,
            request.AllowedChannels.ToArray());

        if (problems.Count > 0)
        {
            await AuditAsync("RegisterProductRoute", request.ProductId, false, problems, context.CancellationToken);

            return ToOperationResponse(false, problems);
        }

        var result = await registerProductRoute.HandleAsync(
            new ProductRoutes.RegisterProductRouteCommand(
                request.ProductId,
                request.AllowedChannels.ToArray(),
                request.AllowedQueueAddresses.ToArray()),
            context.CancellationToken);

        var succeeded = result.Problems.Count == 0;

        await AuditAsync("RegisterProductRoute", request.ProductId, succeeded, result.Problems, context.CancellationToken);

        return ToOperationResponse(succeeded, result.Problems);
    }

    public override async Task<GetProductRouteResponse> GetProductRoute(
        GetProductRouteRequest request,
        ServerCallContext context)
    {
        var result = await getProductRoute.HandleAsync(
            new GetProductRouteQuery(request.ProductId),
            context.CancellationToken);

        if (result.Route is null)
        {
            await AuditAsync("GetProductRoute", request.ProductId, false, result.Problems, context.CancellationToken);

            throw new RpcException(new Status(StatusCode.NotFound, "product route not found"));
        }

        await AuditAsync("GetProductRoute", request.ProductId, true, [], context.CancellationToken);

        var response = new GetProductRouteResponse { ProductId = result.Route.ProductId };
        response.AllowedChannels.AddRange(result.Route.AllowedChannels);
        response.AllowedQueueAddresses.AddRange(result.Route.AllowedQueueAddresses);

        return response;
    }

    public override async Task<GetOutboundOperationResponse> GetOutboundOperation(
        GetOutboundOperationRequest request,
        ServerCallContext context)
    {
        var operation = await db.OutboundOperations
            .AsNoTracking()
            .SingleOrDefaultAsync(entity => entity.MessageId == request.MessageId, context.CancellationToken);

        if (operation is null)
        {
            await AuditAsync("GetOutboundOperation", request.MessageId, false, ["operation: не найдена"], context.CancellationToken);

            throw new RpcException(new Status(StatusCode.NotFound, "operation not found"));
        }

        var log = await db.OutboundMessageLogs
            .AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.MessageId == request.MessageId, context.CancellationToken);

        await AuditAsync("GetOutboundOperation", request.MessageId, true, [], context.CancellationToken);

        return new GetOutboundOperationResponse
        {
            MessageId = operation.MessageId,
            ProductId = operation.ProductId,
            ChannelName = operation.ChannelName,
            Status = operation.Status.ToString(),
            Outcome = operation.Outcome ?? string.Empty,
            HttpStatusCode = operation.HttpStatusCode ?? log?.HttpStatusCode ?? 0,
            ErrorCode = operation.ErrorCode ?? string.Empty,
            AttemptCount = operation.AttemptCount,
            ReturnAddress = operation.ReturnAddress ?? string.Empty,
        };
    }
}
