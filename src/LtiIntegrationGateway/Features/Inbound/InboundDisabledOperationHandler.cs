using LtiIntegrationGateway.Infrastructure.Management;
using LtiIntegrationGateway.Infrastructure.Routing;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;

namespace LtiIntegrationGateway.Features.Inbound;

/// <summary>
/// Обработчик вызова отключённой операции: тело запроса не читается, аутентификация не выполняется,
/// обращение фиксируется в аудите, внешняя система получает <c>404</c> без тела (ADR §7.3, §13).
/// Объект и детали записи аудита усекаются до длин модели.
/// </summary>
public sealed class InboundDisabledOperationHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<InboundDisabledOperationHandler> logger)
{
    /// <summary>Имя операции в аудите: путь известен, но операция отключена.</summary>
    public const string AuditOperation = "InboundOperationDisabled";

    /// <summary>Максимальная длина объекта операции в аудите; совпадает с длиной в модели.</summary>
    public const int MaxTargetLength = 256;

    /// <summary>Максимальная длина деталей записи аудита; совпадает с длиной в модели.</summary>
    public const int MaxDetailsLength = 2048;

    public async Task HandleAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpoint = context.GetEndpoint();
        var route = endpoint?.Metadata.GetMetadata<InboundRouteMetadata>();
        var template = (endpoint as RouteEndpoint)?.RoutePattern.RawText ?? string.Empty;
        var path = RawPath(context);

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
                Truncate($"{channelName}:{method.ToUpperInvariant()} {template}", MaxTargetLength),
                false,
                Truncate(path, MaxDetailsLength),
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

    private static string RawPath(HttpContext context)
    {
        var target = context.Features.Get<IHttpRequestFeature>()?.RawTarget;

        if (string.IsNullOrEmpty(target))
        {
            return context.Request.Path.Value ?? string.Empty;
        }

        var queryIndex = target.IndexOf('?', StringComparison.Ordinal);

        return queryIndex < 0 ? target : target[..queryIndex];
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
