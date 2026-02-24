using System.Reflection;

namespace Jellycheckr.Server.Infrastructure;

internal static class EmbeddedConfigUiHostPage
{
    private const string ResourceSuffix = ".Web.jellycheckr-config-ui-host.html";

    private static readonly Lazy<(bool Found, string? Html)> s_page = new(LoadPage);

    public static bool TryGetHtml(out string html)
    {
        var page = s_page.Value;
        html = page.Html ?? string.Empty;
        return page.Found;
    }

    private static (bool Found, string? Html) LoadPage()
    {
        var assembly = typeof(EmbeddedConfigUiHostPage).Assembly;
        var resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(x => x.EndsWith(ResourceSuffix, StringComparison.Ordinal));

        if (resourceName is null)
        {
            return (false, null);
        }

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return (false, null);
        }

        using var reader = new StreamReader(stream);
        return (true, reader.ReadToEnd());
    }
}
