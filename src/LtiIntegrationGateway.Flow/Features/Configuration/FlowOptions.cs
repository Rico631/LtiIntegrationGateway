using LtiIntegrationGateway.Infrastructure.Rebus;

namespace LtiIntegrationGateway.Features.Configuration;

/// <summary>Настройки экземпляра сервиса потоков.</summary>
public sealed class FlowOptions
{
    /// <summary>Секция конфигурации.</summary>
    public const string SectionName = "Gateway";

    /// <summary>
    /// Интеграция, которую обслуживает экземпляр; формат <c>^[a-z][a-z0-9-]{1,31}$</c>.
    /// Значение обязательно: экземпляр обслуживает ровно одну интеграцию.
    /// </summary>
    public string Integration { get; set; } = string.Empty;

    /// <summary>
    /// Идентификатор экземпляра для отчёта о применённой версии и reply-очереди. Включает идентификатор
    /// процесса: реплики на одном хосте не должны делить строку состояния и очередь ответов.
    /// </summary>
    public string InstanceId { get; set; } =
        $"{Environment.MachineName.ToLowerInvariant()}-{Environment.ProcessId}";

    /// <summary>Интервал сверки версии конфигурации в секундах.</summary>
    public int PollIntervalSeconds { get; set; } = 2;

    /// <summary>Проверяет допустимость значений.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Integration))
        {
            throw new InvalidOperationException("Ключ 'Gateway:Integration' обязателен.");
        }

        if (!QueueNames.IsIntegrationId(Integration))
        {
            throw new InvalidOperationException(
                "Ключ 'Gateway:Integration' не соответствует шаблону ^[a-z][a-z0-9-]{1,31}$.");
        }

        if (string.IsNullOrWhiteSpace(InstanceId))
        {
            throw new InvalidOperationException("Ключ 'Gateway:InstanceId' обязателен.");
        }

        if (PollIntervalSeconds <= 0)
        {
            throw new InvalidOperationException("Ключ 'Gateway:PollIntervalSeconds' должен быть положительным.");
        }
    }
}

