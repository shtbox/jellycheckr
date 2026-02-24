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
    public void UpdateAdminConfig_RejectsServerFallbackWithNoEnabledThresholds()
    {
        var service = new ConfigService(NullLogger<ConfigService>.Instance);
        var invalid = new Jellycheckr.Server.Models.PluginConfig
        {
            EnforcementMode = EnforcementMode.ServerFallback,
            ServerFallbackEpisodeThreshold = 0,
            ServerFallbackMinutesThreshold = 0
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => service.UpdateAdminConfig(invalid));
    }
}
