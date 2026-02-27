using Jellycheckr.Contracts;
using MediaBrowser.Model.Plugins;
using Microsoft.Extensions.Logging;

namespace Jellycheckr.Server.Models;

public class PluginConfig : BasePluginConfiguration
{
    public bool Enabled { get; set; } = true;
    public bool EnableEpisodeCheck { get; set; } = true;
    public bool EnableTimerCheck { get; set; } = true;
    public bool EnableServerFallback { get; set; } = true;
    public int EpisodeThreshold { get; set; } = 3;
    public int MinutesThreshold { get; set; } = 120;
    public int InteractionQuietSeconds { get; set; } = 45;
    public int PromptTimeoutSeconds { get; set; } = 60;
    public int CooldownMinutes { get; set; } = 30;

    // Legacy mode/threshold fields retained to gracefully load older persisted configs.
    public EnforcementMode EnforcementMode { get; set; } = EnforcementMode.WebOnly;
    public int ServerFallbackEpisodeThreshold { get; set; } = 3;
    public int ServerFallbackMinutesThreshold { get; set; } = 120;
    public ServerFallbackTriggerMode ServerFallbackTriggerMode { get; set; } = ServerFallbackTriggerMode.Any;

    public int ServerFallbackInactivityMinutes { get; set; } = 30;
    public bool ServerFallbackPauseBeforeStop { get; set; } = true;
    public int ServerFallbackPauseGraceSeconds { get; set; } = 45;
    public bool ServerFallbackSendMessageBeforePause { get; set; } = true;
    public string ServerFallbackClientMessage { get; set; } = "Are you still watching? Playback will stop soon unless you resume.";
    public bool ServerFallbackDryRun { get; set; }
    public LogLevel MinimumLogLevel { get; set; } = LogLevel.Warning;
    public bool DebugLogging { get; set; }
    public bool DeveloperMode { get; set; }
    public int DeveloperPromptAfterSeconds { get; set; } = 15;
    public int SchemaVersion { get; set; } = 3;
}

public sealed class UserConfigOverride : PluginConfig
{
    public string UserId { get; set; } = string.Empty;
}
