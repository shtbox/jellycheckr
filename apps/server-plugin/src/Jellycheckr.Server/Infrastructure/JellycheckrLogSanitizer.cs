namespace Jellycheckr.Server.Infrastructure;

internal static class JellycheckrLogSanitizer
{
    public static string RedactIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "(none)";
        }

        var trimmed = value.Trim();
        if (trimmed.Length <= 4)
        {
            return new string('*', trimmed.Length);
        }

        return $"{trimmed[..2]}***{trimmed[^2..]}";
    }

    public static string SanitizeSingleLine(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }
}
