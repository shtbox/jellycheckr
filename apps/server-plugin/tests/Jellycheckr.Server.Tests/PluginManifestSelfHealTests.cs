using System.Text.Json.Nodes;
using Jellycheckr.Server.Infrastructure;

namespace Jellycheckr.Server.Tests;

public sealed class PluginManifestSelfHealTests
{
    private static readonly Guid PluginId = Guid.Parse("a53af988-9d8f-4a7c-8d5f-f902fd90e4bd");

    [Fact]
    public void TryRepair_WhenManifestVersionMismatched_RewritesVersionAndLogsRestartWarning()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            var assemblyPath = Path.Combine(tempDirectory.FullName, "Jellycheckr.Server.dll");
            var metaPath = Path.Combine(tempDirectory.FullName, "meta.json");

            File.WriteAllText(assemblyPath, string.Empty);
            File.WriteAllText(
                metaPath,
                """
                {
                  "guid": "a53af988-9d8f-4a7c-8d5f-f902fd90e4bd",
                  "name": "Jellycheckr AYSW",
                  "version": "0.1.2",
                  "packageVersion": "0.1.2",
                  "targetAbi": "10.11.8.0",
                  "dependencies": []
                }
                """);

            var logger = new ListLogger<Plugin>();

            var repaired = PluginManifestSelfHeal.TryRepair(assemblyPath, PluginId, "0.1.2.0", logger);

            Assert.True(repaired);

            var repairedManifest = JsonNode.Parse(File.ReadAllText(metaPath))!.AsObject();
            Assert.Equal("0.1.2.0", repairedManifest["version"]?.GetValue<string>());
            Assert.Equal("0.1.2", repairedManifest["packageVersion"]?.GetValue<string>());
            Assert.Equal("10.11.8.0", repairedManifest["targetAbi"]?.GetValue<string>());
            Assert.Contains(
                logger.Entries,
                entry => entry.Level == LogLevel.Warning
                    && entry.Message.Contains("Repaired plugin manifest version", StringComparison.Ordinal)
                    && entry.Message.Contains("Restart Jellyfin once more", StringComparison.Ordinal));
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }

    [Fact]
    public void TryRepair_WhenManifestVersionMatches_DoesNotRewriteFile()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            var assemblyPath = Path.Combine(tempDirectory.FullName, "Jellycheckr.Server.dll");
            var metaPath = Path.Combine(tempDirectory.FullName, "meta.json");
            const string expectedManifest =
                """
                {
                  "guid": "a53af988-9d8f-4a7c-8d5f-f902fd90e4bd",
                  "version": "0.1.2.0",
                  "packageVersion": "0.1.2",
                  "dependencies": []
                }
                """;

            File.WriteAllText(assemblyPath, string.Empty);
            File.WriteAllText(metaPath, expectedManifest);
            var originalContents = File.ReadAllText(metaPath);
            var logger = new ListLogger<Plugin>();

            var repaired = PluginManifestSelfHeal.TryRepair(assemblyPath, PluginId, "0.1.2.0", logger);

            Assert.False(repaired);
            Assert.Equal(originalContents, File.ReadAllText(metaPath));
            Assert.DoesNotContain(logger.Entries, entry => entry.Message.Contains("Repaired plugin manifest version", StringComparison.Ordinal));
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }
}
