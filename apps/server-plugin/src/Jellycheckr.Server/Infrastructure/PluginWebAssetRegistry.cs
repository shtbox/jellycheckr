namespace Jellycheckr.Server.Infrastructure;

internal static class PluginWebAssetRegistry
{
    public const string WebClientBundleKey = "web-client-bundle";
    public const string ConfigUiBundleKey = "config-ui-bundle";
    public const string ConfigUiStylesKey = "config-ui-styles";
    public const string ConfigUiHostPageKey = "config-ui-host-page";

    private static readonly IReadOnlyDictionary<string, PluginWebAssetDefinition> s_assets =
        new Dictionary<string, PluginWebAssetDefinition>(StringComparer.Ordinal)
        {
            [WebClientBundleKey] = new(
                "jellycheckr-web.js",
                "application/javascript; charset=utf-8",
                "public,max-age=3600"),
            [ConfigUiBundleKey] = new(
                "jellycheckr-config-ui.js",
                "application/javascript; charset=utf-8",
                "public,max-age=300"),
            [ConfigUiStylesKey] = new(
                "jellycheckr-config-ui.css",
                "text/css; charset=utf-8",
                "public,max-age=300"),
            [ConfigUiHostPageKey] = new(
                "jellycheckr-config-ui-host.html",
                "text/html; charset=utf-8",
                "public,max-age=300")
        };

    public static bool TryResolve(
        string key,
        out PluginWebAssetDefinition asset,
        out string absolutePath)
    {
        if (!s_assets.TryGetValue(key, out asset!))
        {
            absolutePath = string.Empty;
            return false;
        }

        absolutePath = Path.Combine(GetWebRootPath(), asset.FileName);
        return true;
    }

    public static bool Exists(string key, out string absolutePath)
    {
        if (!TryResolve(key, out _, out absolutePath))
        {
            return false;
        }

        return File.Exists(absolutePath);
    }

    private static string GetWebRootPath()
    {
        var assemblyPath = Plugin.Instance?.AssemblyFilePath;
        if (string.IsNullOrWhiteSpace(assemblyPath))
        {
            assemblyPath = typeof(PluginWebAssetRegistry).Assembly.Location;
        }

        var pluginDirectory = Path.GetDirectoryName(assemblyPath);
        if (string.IsNullOrWhiteSpace(pluginDirectory))
        {
            pluginDirectory = AppContext.BaseDirectory;
        }

        return Path.Combine(pluginDirectory, "web");
    }
}

internal sealed record PluginWebAssetDefinition(
    string FileName,
    string ContentType,
    string CacheControl);
