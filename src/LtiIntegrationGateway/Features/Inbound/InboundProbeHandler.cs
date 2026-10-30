using LtiIntegrationGateway.Infrastructure.Routing;
using Microsoft.AspNetCore.Http.Features;

namespace LtiIntegrationGateway.Features.Inbound;

/// <summary>Диагностический ответ динамического маршрута: подтверждает фактически выбранный маршрут.</summary>
public sealed record InboundProbeResponse(
    string HttpMethod,
    string RouteTemplate,
    string RelativePath,
    string Channel);

/// <summary>
/// Обработчик динамических входящих маршрутов. В рамках спайка только подтверждает выбор маршрута:
/// не публикует сообщения в шину и не обращается к хранилищу.
/// </summary>
public sealed class InboundProbeHandler
{
    public Task HandleAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var routeTemplate = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? string.Empty;
        var channel = context.GetEndpoint()?.Metadata.GetMetadata<InboundChannelMetadata>()?.ChannelName ?? string.Empty;

        return context.Response.WriteAsJsonAsync(new InboundProbeResponse(
            context.Request.Method,
            routeTemplate,
            RawPath(context),
            channel));
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
}
