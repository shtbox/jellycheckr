using System.Reflection;
using Jellycheckr.Contracts;
using Jellycheckr.Server.Infrastructure;
using Jellycheckr.Server.Models;
using Microsoft.Extensions.Logging;

namespace Jellycheckr.Server.Services;

public interface IConfigService
{
    EffectiveConfigResponse GetEffectiveConfig(string? userId);
    PluginConfig GetAdminConfig();
    PluginConfig UpdateAdminConfig(PluginConfig next);
}

public sealed class ConfigService : IConfigService
{
    private readonly ILogger<ConfigService> _logger;

    public ConfigService(ILogger<ConfigService> logger)
    {
        _logger = logger;
    }

    public EffectiveConfigResponse GetEffectiveConfig(string? userId)
    {
        var config = GetAdminConfig();
        var effective = ToEffectiveResponse(config);
        JellycheckrDiagnosticLogging.Verbose(
            _logger,
            config,
            "Computed effective config for userId={UserId} adminConfig={@AdminConfig} effectiveConfig={@EffectiveConfig}",
            userId ?? "(null)",
            JellycheckrDiagnosticLogging.Describe(config),
            JellycheckrDiagnosticLogging.Describe(effective));
        return effective;
    }

    public PluginConfig GetAdminConfig()
    {
        var config = Plugin.Instance?.Configuration;
        var resolved = config ?? new PluginConfig();
        JellycheckrDiagnosticLogging.Verbose(
            _logger,
            resolved,
            "Resolved admin config from plugin instance present={HasPluginInstance} config={@Config}",
            Plugin.Instance is not null,
            JellycheckrDiagnosticLogging.Describe(resolved));
        return resolved;
    }

    public PluginConfig UpdateAdminConfig(PluginConfig next)
    {
        _logger.LogInformation("[Jellycheckr] Updating admin configuration.");
        JellycheckrDiagnosticLogging.Verbose(
            _logger,
            next,
            "Validating incoming admin config payload={@Config}",
            JellycheckrDiagnosticLogging.Describe(next));
        Validate(next);
        if (Plugin.Instance is null)
        {
            _logger.LogWarning("[Jellycheckr] Plugin instance was null during config update; returning validated payload only.");
            return next;
        }

        var previous = Plugin.Instance.Configuration;
        JellycheckrDiagnosticLogging.Verbose(
            _logger,
            next,
            "Persisting admin config previous={@PreviousConfig} next={@NextConfig}",
            previous is null ? null : JellycheckrDiagnosticLogging.Describe(previous),
            JellycheckrDiagnosticLogging.Describe(next));
        SetPluginConfiguration(Plugin.Instance, next);
        Plugin.Instance.SaveConfiguration();
        var saved = Plugin.Instance.Configuration;
        JellycheckrDiagnosticLogging.Verbose(
            _logger,
            saved,
            "Saved admin config persisted={@Config}",
            JellycheckrDiagnosticLogging.Describe(saved));
        return saved;
    }

    /// <summary>
    /// Set the plugin's Configuration so that script/API updates persist via the host.
    /// The base property setter is protected; we use reflection so admin API and dashboard stay in sync.
    /// </summary>
    private static void SetPluginConfiguration(Plugin plugin, PluginConfig config)
    {
        var prop = plugin.GetType().BaseType?.GetProperty("Configuration", BindingFlags.Public | BindingFlags.Instance);
        prop?.SetValue(plugin, config);
    }

    private static EffectiveConfigResponse ToEffectiveResponse(PluginConfig config)
    {
        return new EffectiveConfigResponse
        {
            Enabled = config.Enabled,
            EpisodeThreshold = config.EpisodeThreshold,
            MinutesThreshold = config.MinutesThreshold,
            InteractionQuietSeconds = config.InteractionQuietSeconds,
            PromptTimeoutSeconds = config.PromptTimeoutSeconds,
            CooldownMinutes = config.CooldownMinutes,
            EnforcementMode = config.EnforcementMode,
            ServerFallbackEpisodeThreshold = config.ServerFallbackEpisodeThreshold,
            ServerFallbackMinutesThreshold = config.ServerFallbackMinutesThreshold,
            ServerFallbackTriggerMode = config.ServerFallbackTriggerMode,
            ServerFallbackInactivityMinutes = config.ServerFallbackInactivityMinutes,
            ServerFallbackPauseBeforeStop = config.ServerFallbackPauseBeforeStop,
            ServerFallbackPauseGraceSeconds = config.ServerFallbackPauseGraceSeconds,
            ServerFallbackSendMessageBeforePause = config.ServerFallbackSendMessageBeforePause,
            ServerFallbackClientMessage = string.IsNullOrWhiteSpace(config.ServerFallbackClientMessage)
                ? "Are you still watching? Playback will stop soon unless you resume."
                : config.ServerFallbackClientMessage.Trim(),
            ServerFallbackDryRun = config.ServerFallbackDryRun,
            DebugLogging = config.DebugLogging,
            DeveloperMode = config.DeveloperMode,
            DeveloperPromptAfterSeconds = config.DeveloperPromptAfterSeconds,
            Version = config.SchemaVersion,
            SchemaVersion = config.SchemaVersion
        };
    }

    private static void Validate(PluginConfig config)
    {
        if (config.EpisodeThreshold < 1) throw new ArgumentOutOfRangeException(nameof(config.EpisodeThreshold));
        if (config.MinutesThreshold < 1) throw new ArgumentOutOfRangeException(nameof(config.MinutesThreshold));
        if (config.InteractionQuietSeconds < 5) throw new ArgumentOutOfRangeException(nameof(config.InteractionQuietSeconds));
        if (config.PromptTimeoutSeconds < 10) throw new ArgumentOutOfRangeException(nameof(config.PromptTimeoutSeconds));
        if (config.CooldownMinutes < 0) throw new ArgumentOutOfRangeException(nameof(config.CooldownMinutes));
        if (config.ServerFallbackEpisodeThreshold < 0) throw new ArgumentOutOfRangeException(nameof(config.ServerFallbackEpisodeThreshold));
        if (config.ServerFallbackMinutesThreshold < 0) throw new ArgumentOutOfRangeException(nameof(config.ServerFallbackMinutesThreshold));
        if (config.ServerFallbackInactivityMinutes < 1) throw new ArgumentOutOfRangeException(nameof(config.ServerFallbackInactivityMinutes));
        if (config.ServerFallbackPauseGraceSeconds < 5) throw new ArgumentOutOfRangeException(nameof(config.ServerFallbackPauseGraceSeconds));
        if (config.DeveloperPromptAfterSeconds < 1) throw new ArgumentOutOfRangeException(nameof(config.DeveloperPromptAfterSeconds));
        if (config.SchemaVersion < 1) throw new ArgumentOutOfRangeException(nameof(config.SchemaVersion));
        if (config.EnforcementMode == EnforcementMode.ServerFallback
            && config.ServerFallbackEpisodeThreshold < 1
            && config.ServerFallbackMinutesThreshold < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(config.ServerFallbackEpisodeThreshold), "At least one server fallback threshold must be enabled.");
        }
    }
}
