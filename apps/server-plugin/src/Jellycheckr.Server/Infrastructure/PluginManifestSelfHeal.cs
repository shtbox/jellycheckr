using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Jellycheckr.Server.Infrastructure;

internal static class PluginManifestSelfHeal
{
    private const string MetaFileName = "meta.json";

    public static bool TryRepair(string? assemblyFilePath, Guid pluginId, string expectedVersion, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        if (string.IsNullOrWhiteSpace(assemblyFilePath) || string.IsNullOrWhiteSpace(expectedVersion))
        {
            return false;
        }

        var pluginDirectory = Path.GetDirectoryName(assemblyFilePath);
        if (string.IsNullOrWhiteSpace(pluginDirectory))
        {
            return false;
        }

        var metaPath = Path.Combine(pluginDirectory, MetaFileName);
        if (!File.Exists(metaPath))
        {
            return false;
        }

        try
        {
            var manifestRoot = JsonNode.Parse(File.ReadAllText(metaPath)) as JsonObject;
            if (manifestRoot is null)
            {
                logger.LogWarning("[Jellycheckr] Plugin manifest self-heal skipped because {ManifestPath} did not contain a JSON object.", metaPath);
                return false;
            }

            var manifestGuid = manifestRoot["guid"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(manifestGuid)
                && Guid.TryParse(manifestGuid, out var parsedManifestGuid)
                && parsedManifestGuid != pluginId)
            {
                logger.LogWarning("[Jellycheckr] Plugin manifest self-heal skipped because {ManifestPath} did not match plugin id {PluginId}.", metaPath, pluginId);
                return false;
            }

            var currentVersion = manifestRoot["version"]?.GetValue<string>();
            if (string.Equals(currentVersion, expectedVersion, StringComparison.Ordinal))
            {
                return false;
            }

            manifestRoot["version"] = expectedVersion;
            File.WriteAllText(metaPath, manifestRoot.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            logger.LogWarning(
                "[Jellycheckr] Repaired plugin manifest version at {ManifestPath} from {PreviousVersion} to {ExpectedVersion}. Restart Jellyfin once more before relying on disable or uninstall actions.",
                metaPath,
                string.IsNullOrWhiteSpace(currentVersion) ? "<missing>" : currentVersion,
                expectedVersion);

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning(ex, "[Jellycheckr] Plugin manifest self-heal failed for {ManifestPath}.", metaPath);
            return false;
        }
    }
}
