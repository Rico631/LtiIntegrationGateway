namespace LtiIntegrationGateway.Features.Outbound;

/// <summary>Структурная проверка относительного пути, предотвращающая SSRF (ADR §8.7).</summary>
internal static class RelativePathValidator
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
}
