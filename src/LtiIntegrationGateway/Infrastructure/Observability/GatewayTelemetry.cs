using System.Diagnostics;

namespace LtiIntegrationGateway.Infrastructure.Observability;

/// <summary>Единые имена источников, инструментов и безопасные операции с тегами телеметрии.</summary>
public static class GatewayTelemetry
{
    /// <summary>Имя источника ActivitySource шлюза.</summary>
    public const string ActivitySourceName = "LtiIntegrationGateway";

    /// <summary>Имя Meter шлюза.</summary>
    public const string MeterName = "LtiIntegrationGateway";

    /// <summary>Версия телеметрии из версии сборки.</summary>
    public static readonly string Version =
        typeof(GatewayTelemetry).Assembly.GetName().Version?.ToString() ?? "unknown";

    /// <summary>Span входящего HTTP-вызова.</summary>
    public const string InboundRequestActivity = "inbound.request";

    /// <summary>Span обработки исходящей операции.</summary>
    public const string OutboundOperationActivity = "outbound.operation";

    /// <summary>Span попытки доставки сохранённого результата.</summary>
    public const string OutboundDeliveryActivity = "outbound.result.delivery";

    /// <summary>Источник бизнес-трассировок шлюза.</summary>
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, Version);

    /// <summary>Направление inbound.</summary>
    public const string InboundDirection = "inbound";

    /// <summary>Направление outbound.</summary>
    public const string OutboundDirection = "outbound";

    /// <summary>Имя тега канала.</summary>
    public const string ChannelTag = "channel";

    /// <summary>Имя тега метода HTTP.</summary>
    public const string HttpMethodTag = "http.request.method";

    /// <summary>Имя тега шаблона маршрута.</summary>
    public const string HttpRouteTag = "http.route";

    /// <summary>Имя тега пути без query-параметров.</summary>
    public const string UrlPathTag = "url.path";

    /// <summary>Имя тега идентификатора сообщения.</summary>
    public const string MessageIdTag = "message_id";

    /// <summary>Имя тега идентификатора корреляции.</summary>
    public const string CorrelationIdTag = "correlation_id";

    /// <summary>Имя тега продукта.</summary>
    public const string ProductIdTag = "product_id";

    /// <summary>Имя тега направления.</summary>
    public const string DirectionTag = "direction";

    /// <summary>Имя тега идентификатора операции OpenAPI.</summary>
    public const string OperationIdTag = "operation_id";

    /// <summary>Имя тега исхода.</summary>
    public const string OutcomeTag = "outcome";

    /// <summary>Имя тега безопасного кода ошибки.</summary>
    public const string ErrorCodeTag = "error_code";

    /// <summary>Режим ответа входящего канала: async или sync; не содержит payload.</summary>
    public const string InboundResponseModeTag = "inbound.response_mode";

    /// <summary>Имя тега HTTP-статуса ответа.</summary>
    public const string HttpStatusCodeTag = "http.response.status_code";

    /// <summary>Имя тега числа попыток.</summary>
    public const string AttemptCountTag = "attempt_count";

    /// <summary>Имя тега номера ревизии канала.</summary>
    public const string ChannelRevisionTag = "channel.revision";

    /// <summary>Имя тега результата доставки.</summary>
    public const string DeliverySucceededTag = "delivery.succeeded";

    /// <summary>Запускает activity; при отсутствии слушателей возвращает <c>null</c>.</summary>
    public static Activity? StartActivity(string name, ActivityKind kind = ActivityKind.Internal) =>
        ActivitySource.StartActivity(name, kind);

    /// <summary>Устанавливает строковый тег только для непустого значения.</summary>
    public static void SetTag(Activity? activity, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            activity?.SetTag(name, value);
        }
    }

    /// <summary>Устанавливает числовой тег только для заданного значения.</summary>
    public static void SetTag(Activity? activity, string name, int? value)
    {
        if (value.HasValue)
        {
            activity?.SetTag(name, value.Value);
        }
    }

    /// <summary>Устанавливает логический тег только для заданного значения.</summary>
    public static void SetTag(Activity? activity, string name, bool? value)
    {
        if (value.HasValue)
        {
            activity?.SetTag(name, value.Value);
        }
    }
}
