using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;

namespace LtiIntegrationGateway.Features.Channels;

/// <summary>Проверка конфигурации канала перед публикацией ревизии.</summary>
/// <remarks>
/// Возвращает список проблем с именами полей: «конфигурация не прошла проверку» — ожидаемый
/// исход операции, а не сбой процесса.
/// </remarks>
public sealed class ChannelConfigurationValidator
{
    private const int MaxDelaysPerRule = 5;

    public IReadOnlyList<string> Validate(ChannelConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var problems = new List<string>();

        ValidateBaseUrl(configuration.BaseUrl, problems);
        ValidateCredentials(configuration, problems);
        ValidateTimeouts(configuration, problems);
        ValidateRetryPolicy(configuration.RetryPolicy, problems);

        return problems;
    }

    private static void ValidateBaseUrl(string baseUrl, List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            problems.Add("base_url: обязательное поле не заполнено");
            return;
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            problems.Add("base_url: должен быть абсолютным адресом");
            return;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            problems.Add("base_url: допускается только схема https");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            problems.Add("base_url: учётные данные в адресе не допускаются");
        }
    }

    private static void ValidateCredentials(ChannelConfiguration configuration, List<string> problems)
    {
        var requiresSecret = configuration.AuthType is not ChannelAuthType.None;

        if (requiresSecret && string.IsNullOrWhiteSpace(configuration.ConfigKey))
        {
            problems.Add("config_key: для выбранного типа авторизации ключ конфигурации обязателен");
        }
    }

    private static void ValidateTimeouts(ChannelConfiguration configuration, List<string> problems)
    {
        if (configuration.PerAttemptTimeoutMs <= 0)
        {
            problems.Add("per_attempt_timeout_ms: значение должно быть положительным");
        }

        if (configuration.OverallCapMs <= 0)
        {
            problems.Add("overall_cap_ms: значение должно быть положительным");
        }

        if (configuration is { OverallCapMs: > 0, PerAttemptTimeoutMs: > 0 } &&
            configuration.OverallCapMs < configuration.PerAttemptTimeoutMs)
        {
            problems.Add("overall_cap_ms: общий предел не может быть меньше таймаута попытки");
        }
    }

    private static void ValidateRetryPolicy(RetryPolicy policy, List<string> problems)
    {
        if (policy.Rules.Count == 0)
        {
            problems.Add("retry_policy: должно быть указано хотя бы одно правило");
            return;
        }

        for (var index = 0; index < policy.Rules.Count; index++)
        {
            var rule = policy.Rules[index];
            var prefix = $"retry_policy[{index}]";

            if (rule.DelaysMs.Count is 0 or > MaxDelaysPerRule)
            {
                problems.Add($"{prefix}.delays: допускается от 1 до {MaxDelaysPerRule} задержек");
            }

            if (rule.DelaysMs.Any(delay => delay <= 0))
            {
                problems.Add($"{prefix}.delays: задержки должны быть положительными");
            }

            switch (rule.Match)
            {
                case RetryMatchType.TransportError when rule.StatusCodes.Count > 0:
                    problems.Add($"{prefix}.status_codes: для транспортной ошибки статусы не задаются");
                    break;

                case RetryMatchType.HttpStatusIn when rule.StatusCodes.Count == 0:
                    problems.Add($"{prefix}.status_codes: должен быть указан хотя бы один статус");
                    break;

                case RetryMatchType.HttpStatusIn when rule.StatusCodes.Any(code => code is < 100 or > 599):
                    problems.Add($"{prefix}.status_codes: статус вне диапазона 100..599");
                    break;
            }
        }
    }
}