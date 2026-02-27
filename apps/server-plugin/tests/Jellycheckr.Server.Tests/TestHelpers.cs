using Jellycheckr.Contracts;
using Jellycheckr.Server.Infrastructure;
using Jellycheckr.Server.Models;
using Jellycheckr.Server.Services;

namespace Jellycheckr.Server.Tests;

internal sealed class FakeClock : IClock
{
    public FakeClock(DateTimeOffset utcNow) => UtcNow = utcNow;
    public DateTimeOffset UtcNow { get; set; }
}

internal sealed class StubConfigService : IConfigService
{
    private readonly PluginConfig _adminConfig;

    public StubConfigService(PluginConfig? adminConfig = null)
    {
        _adminConfig = adminConfig ?? new PluginConfig();
    }

    public EffectiveConfigResponse GetEffectiveConfig(string? userId)
    {
        return new EffectiveConfigResponse
        {
            Enabled = _adminConfig.Enabled,
            EnableEpisodeCheck = _adminConfig.EnableEpisodeCheck,
            EnableTimerCheck = _adminConfig.EnableTimerCheck,
            EnableServerFallback = _adminConfig.EnableServerFallback,
            EpisodeThreshold = _adminConfig.EpisodeThreshold,
            MinutesThreshold = _adminConfig.MinutesThreshold,
            InteractionQuietSeconds = _adminConfig.InteractionQuietSeconds,
            PromptTimeoutSeconds = _adminConfig.PromptTimeoutSeconds,
            CooldownMinutes = _adminConfig.CooldownMinutes,
            ServerFallbackInactivityMinutes = _adminConfig.ServerFallbackInactivityMinutes,
            ServerFallbackPauseBeforeStop = _adminConfig.ServerFallbackPauseBeforeStop,
            ServerFallbackPauseGraceSeconds = _adminConfig.ServerFallbackPauseGraceSeconds,
            ServerFallbackSendMessageBeforePause = _adminConfig.ServerFallbackSendMessageBeforePause,
            ServerFallbackClientMessage = _adminConfig.ServerFallbackClientMessage,
            ServerFallbackDryRun = _adminConfig.ServerFallbackDryRun,
            DebugLogging = _adminConfig.DebugLogging,
            DeveloperMode = _adminConfig.DeveloperMode,
            DeveloperPromptAfterSeconds = _adminConfig.DeveloperPromptAfterSeconds,
            Version = _adminConfig.SchemaVersion,
            SchemaVersion = _adminConfig.SchemaVersion
        };
    }

    public PluginConfig GetAdminConfig() => _adminConfig;

    public PluginConfig UpdateAdminConfig(PluginConfig next)
    {
        throw new NotSupportedException("StubConfigService does not persist config.");
    }
}
