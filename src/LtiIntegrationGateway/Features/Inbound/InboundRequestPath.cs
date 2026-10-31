using Microsoft.AspNetCore.Http.Features;

namespace LtiIntegrationGateway.Features.Inbound;

/// <summary>
/// Фактический путь запроса без query string и без percent-декодирования: единая реализация для приёма
/// вызова и обработчика отключённой операции.
/// </summary>
internal static class InboundRequestPath
{
    public static string Get(HttpContext context)
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
