using LtiIntegrationGateway.Infrastructure.Management;
using LtiIntegrationGateway.Infrastructure.Routing;

namespace LtiIntegrationGateway.Features.Inbound;

/// <summary>
/// Обработчик вызова отключённой операции: тело запроса не читается, аутентификация не выполняется,
/// обращение фиксируется в аудите, внешняя система получает <c>404</c> без тела (ADR §7.3, §13).
/// </summary>
public sealed class InboundDisabledOperationHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<InboundDisabledOperationHandler> logger)
{
    /// <summary>Имя операции в аудите: путь известен, но операция отключена.</summary>
    public const string AuditOperation = "InboundOperationDisabled";

    public async Task HandleAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpoint = context.GetEndpoint();
        var route = endpoint?.Metadata.GetMetadata<InboundRouteMetadata>();
        var template = (endpoint as RouteEndpoint)?.RoutePattern.RawText ?? string.Empty;
        var path = InboundRequestPath.Get(context);

        if (route is not null)
        {
            await RecordAsync(route.ChannelName, context.Request.Method, template, path);
        }

        context.Response.StatusCode = StatusCodes.Status404NotFound;
    }

    private async Task RecordAsync(string channelName, string method, string template, string path)
    {
        try
        {
            // Собственный scope: endpoint живёт дольше запроса, а аудит пишется в отдельный DbContext.
            await using var scope = scopeFactory.CreateAsyncScope();
            var audit = scope.ServiceProvider.GetRequiredService<ManagementAuditWriter>();

            await audit.RecordAsync(
                AuditOperation,
                $"{channelName}:{method.ToUpperInvariant()} {template}",
                false,
                path,
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            // Недоступность хранилища не меняет ответ внешней системе: вызов всё равно не обслуживается.
            logger.LogError(
                exception,
                "Не удалось записать аудит вызова отключённой операции: канал {Channel}, маршрут {Template}",
                channelName,
                template);
        }
    }
}
