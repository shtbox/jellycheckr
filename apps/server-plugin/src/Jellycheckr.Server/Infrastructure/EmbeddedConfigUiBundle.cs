using System.Reflection;

namespace Jellycheckr.Server.Infrastructure;

internal static class EmbeddedConfigUiBundle
{
    private const string ResourceSuffix = ".Web.jellycheckr-config-ui.js";

    private static readonly Lazy<(bool Found, string? Script)> s_bundle = new(LoadBundle);

    public static bool TryGetBundle(out string script)
    {
        var bundle = s_bundle.Value;
        script = bundle.Script ?? string.Empty;
        return bundle.Found;
    }

    private static (bool Found, string? Script) LoadBundle()
    {
        var assembly = typeof(EmbeddedConfigUiBundle).Assembly;
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
