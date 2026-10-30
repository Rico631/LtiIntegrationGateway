using System.Text;
using Microsoft.AspNetCore.Routing.Patterns;

namespace LtiIntegrationGateway.Infrastructure.Routing;

/// <summary>
/// Проверка набора маршрутов до активации: поддерживаемые методы, разбор шаблонов и отсутствие
/// конфликтов «метод + шаблон» внутри набора и с уже активными наборами.
/// </summary>
public sealed class InboundRouteSetValidator
{
    private static readonly HashSet<string> SupportedMethods =
        new(StringComparer.Ordinal) { "GET", "POST", "PUT", "PATCH", "DELETE" };

    public IReadOnlyList<string> Validate(
        InboundRouteSet set,
        IReadOnlyCollection<InboundRouteSet> activeSets)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(activeSets);

        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(set.RevisionKey))
        {
            problems.Add("revision_key: ключ ревизии обязателен");
        }

        if (set.Routes.Count == 0)
        {
            problems.Add("routes: набор не содержит маршрутов");
        }

        var activeKeys = activeSets
            .Where(active => !string.Equals(active.RevisionKey, set.RevisionKey, StringComparison.Ordinal))
            .SelectMany(active => active.Routes)
            .Select(route => RouteKey(route.HttpMethod, route.Template))
            .Where(key => key is not null)
            .Select(key => key!)
            .ToHashSet(StringComparer.Ordinal);

        var ownKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var route in set.Routes)
        {
            var method = (route.HttpMethod ?? string.Empty).Trim().ToUpperInvariant();
            var template = (route.Template ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(route.ChannelName))
            {
                problems.Add($"channel: маршрут '{method} {template}' не содержит канал");
            }

            if (!SupportedMethods.Contains(method))
            {
                problems.Add($"method: метод '{route.HttpMethod}' не поддерживается");
            }

            var key = RouteKey(method, template);

            if (key is null)
            {
                problems.Add($"template: шаблон '{template}' не разбирается");
                continue;
            }

            if (!ownKeys.Add(key))
            {
                problems.Add($"conflict: маршрут '{method} {template}' повторяется в наборе");
            }

            if (activeKeys.Contains(key))
            {
                problems.Add($"conflict: маршрут '{method} {template}' уже активен в другом наборе");
            }
        }

        return problems;
    }

    /// <summary>
    /// Ключ сравнения маршрутов. Шаблон приводится к структурному виду: параметрические сегменты
    /// заменяются на <c>{}</c>, поэтому <c>/a/{id}</c> и <c>/a/{name}</c> считаются конфликтом.
    /// </summary>
    private static string? RouteKey(string? method, string? template)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return null;
        }

        RoutePattern pattern;

        try
        {
            pattern = RoutePatternFactory.Parse(template);
        }
        catch (RoutePatternException)
        {
            return null;
        }

        return $"{method?.Trim().ToUpperInvariant()} {Normalize(pattern.RawText ?? template)}";
    }

    private static string Normalize(string template)
    {
        var builder = new StringBuilder(template.Length);

        foreach (var segment in template.Split('/'))
        {
            var isParameter = segment.Length > 2 && segment.StartsWith('{') && segment.EndsWith('}');

            builder.Append('/').Append(isParameter ? "{}" : segment.ToLowerInvariant());
        }

        return builder.ToString();
    }
}
