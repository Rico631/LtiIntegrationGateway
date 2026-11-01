using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace LtiIntegrationGateway.Infrastructure.Management;

/// <summary>
/// Аутентификация управляющего API по ключу (ADR §13). Покрывает и REST-запросы (<c>/v1/**</c>),
/// и gRPC-вызовы сервиса управления, потому что оба доступа идут через один HTTP pipeline.
/// </summary>
public sealed class ManagementApiKeyMiddleware(
    RequestDelegate next,
    IOptions<ManagementApiKeyOptions> options)
{
    private static readonly string[] ManagementPathPrefixes =
    [
        "/v1",
        "/lti.gateway.management.v1.ManagementService",
    ];

    private const int UnauthenticatedGrpcStatus = 16;

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!IsManagementPath(context.Request.Path))
        {
            await next(context);

            return;
        }

        var settings = options.Value;

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;

            return;
        }

        var provided = context.Request.Headers[settings.HeaderName].ToString();

        if (!Matches(provided, settings.ApiKey))
        {
            Reject(context);

            return;
        }

        await next(context);
    }

    private static bool IsManagementPath(PathString path) =>
        ManagementPathPrefixes.Any(prefix =>
            path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>Сравнение за постоянное время; длина обрабатывается отдельно, чтобы не утекать.</summary>
    private static bool Matches(string provided, string expected)
    {
        if (string.IsNullOrEmpty(provided))
        {
            return false;
        }

        var providedBytes = Encoding.UTF8.GetBytes(provided);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);

        return providedBytes.Length == expectedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }

    /// <summary>Отказ без деталей: <c>401</c> для REST и trailers-only-ответ для gRPC.</summary>
    private static void Reject(HttpContext context)
    {
        var isGrpc = context.Request.ContentType?.StartsWith("application/grpc", StringComparison.OrdinalIgnoreCase) == true;

        if (isGrpc)
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "application/grpc";
            context.Response.Headers.GrpcStatus = UnauthenticatedGrpcStatus.ToString(CultureInfo.InvariantCulture);
            context.Response.Headers.GrpcMessage = "unauthenticated";

            return;
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
    }
}
