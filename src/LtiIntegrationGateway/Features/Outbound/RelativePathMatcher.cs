namespace LtiIntegrationGateway.Features.Outbound;

/// <summary>Проверка относительного пути и сопоставление с шаблоном канала (ADR §8.7).</summary>
internal static class RelativePathMatcher
{
    private const int MaxLength = 2048;

    public static bool IsStructurallyValid(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > MaxLength)
        {
            return false;
        }

        if (!path.StartsWith('/'))
        {
            return false;
        }

        string[] forbidden = ["://", "//", "..", "#", "?", "\\", "\r", "\n"];

        return !forbidden.Any(token => path.Contains(token, StringComparison.Ordinal));
    }

    /// <summary>Совпадение фактического пути с шаблоном: <c>{name}</c> соответствует одному сегменту.</summary>
    public static bool Matches(string path, string template)
    {
        var pathSegments = path.Split('/');
        var templateSegments = template.Split('/');

        if (pathSegments.Length != templateSegments.Length)
        {
            return false;
        }

        for (var index = 0; index < pathSegments.Length; index++)
        {
            var templateSegment = templateSegments[index];

            var isParameter = templateSegment.Length > 2 &&
                              templateSegment.StartsWith('{') &&
                              templateSegment.EndsWith('}');

            if (!isParameter && !string.Equals(templateSegment, pathSegments[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
