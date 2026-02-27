using Jellycheckr.Server.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Jellycheckr.Contracts;

namespace Jellycheckr.Server.Tests;

public sealed class ConfigServiceTests
{
    [Fact]
    public void UpdateAdminConfig_RejectsInvalidThreshold()
    {
        var service = new ConfigService(NullLogger<ConfigService>.Instance);
        var invalid = new Jellycheckr.Server.Models.PluginConfig { EpisodeThreshold = 0 };
        Assert.Throws<ArgumentOutOfRangeException>(() => service.UpdateAdminConfig(invalid));
    }

    [Fact]
    public void UpdateAdminConfig_RejectsWhenBothChecksDisabled()
    {
        var service = new ConfigService(NullLogger<ConfigService>.Instance);
        var invalid = new Jellycheckr.Server.Models.PluginConfig
        {
            EnableEpisodeCheck = false,
            EnableTimerCheck = false,
            SchemaVersion = 3
        };

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => service.UpdateAdminConfig(invalid));
        Assert.Contains("At least one threshold check must be enabled", ex.Message);
    }

    [Fact]
    public void UpdateAdminConfig_MigratesLegacyServerFallbackConfigToSchemaV3()
    {
        var service = new ConfigService(NullLogger<ConfigService>.Instance);
        var legacy = new Jellycheckr.Server.Models.PluginConfig
        {
            SchemaVersion = 2,
            EnforcementMode = EnforcementMode.ServerFallback,
            EnableEpisodeCheck = false,
            EnableTimerCheck = false,
            EnableServerFallback = false,
            EpisodeThreshold = 3,
            MinutesThreshold = 120,
            ServerFallbackEpisodeThreshold = 0,
            ServerFallbackMinutesThreshold = 90
        };

        var migrated = service.UpdateAdminConfig(legacy);

        Assert.Equal(3, migrated.SchemaVersion);
        Assert.False(migrated.EnableEpisodeCheck);
        Assert.True(migrated.EnableTimerCheck);
        Assert.True(migrated.EnableServerFallback);
        Assert.Equal(90, migrated.MinutesThreshold);
    }
}
