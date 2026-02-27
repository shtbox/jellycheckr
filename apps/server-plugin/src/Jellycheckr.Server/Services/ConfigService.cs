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
    private const int CurrentSchemaVersion = 3;
    private readonly ILogger<ConfigService> _logger;

    public ConfigService(ILogger<ConfigService> logger)
    {
        _logger = logger;
    }

    public EffectiveConfigResponse GetEffectiveConfig(string? userId)
    {
        var config = GetAdminConfig();
        var effective = ToEffectiveResponse(config);
        _logger.LogJellycheckrTrace(
            "Computed effective config for userId={UserId} adminConfig={@AdminConfig} effectiveConfig={@EffectiveConfig}",
            userId,
            config,
            effective);
        return effective;
    }

    public PluginConfig GetAdminConfig()
    {
        var config = Plugin.Instance?.Configuration;
        var resolved = config ?? new PluginConfig();
        var migrated = MigrateLegacyConfigIfNeeded(resolved);
        Validate(resolved);
        JellycheckrLogLevelState.Apply(resolved);

        if (migrated && Plugin.Instance is not null)
        {
            _logger.LogJellycheckrInformation(
                "[Jellycheckr] Migrating admin configuration to schema v{SchemaVersion}.",
                CurrentSchemaVersion);
            SetPluginConfiguration(Plugin.Instance, resolved);
            Plugin.Instance.SaveConfiguration();
        }

        _logger.LogJellycheckrTrace(
            "Resolved admin config from plugin instance present={HasPluginInstance} migrated={Migrated} config={@Config}",
            Plugin.Instance is not null,
            migrated,
            resolved);
        return resolved;
    }

    public PluginConfig UpdateAdminConfig(PluginConfig next)
    {
        _ = MigrateLegacyConfigIfNeeded(next);
        Validate(next);
        JellycheckrLogLevelState.Apply(next);
        _logger.LogJellycheckrInformation("[Jellycheckr] Updating admin configuration.");
        _logger.LogJellycheckrTrace("Validated incoming admin config payload={@Config}", next);
        if (Plugin.Instance is null)
        {
            _logger.LogJellycheckrWarning("[Jellycheckr] Plugin instance was null during config update; returning validated payload only.");
            return next;
        }

        var previous = Plugin.Instance.Configuration;
        _logger.LogJellycheckrTrace(
            "Persisting admin config previous={@PreviousConfig} next={@NextConfig}",
            previous,
            next);
        SetPluginConfiguration(Plugin.Instance, next);
        Plugin.Instance.SaveConfiguration();
        var saved = Plugin.Instance.Configuration;
        _logger.LogJellycheckrTrace("Saved admin config persisted={@Config}", saved);
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
            EnableEpisodeCheck = config.EnableEpisodeCheck,
            EnableTimerCheck = config.EnableTimerCheck,
            EnableServerFallback = config.EnableServerFallback,
            EpisodeThreshold = config.EpisodeThreshold,
            MinutesThreshold = config.MinutesThreshold,
            InteractionQuietSeconds = config.InteractionQuietSeconds,
            PromptTimeoutSeconds = config.PromptTimeoutSeconds,
            CooldownMinutes = config.CooldownMinutes,
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

    private static bool MigrateLegacyConfigIfNeeded(PluginConfig config)
    {
        var migrated = false;

        if (config.SchemaVersion < CurrentSchemaVersion)
        {
            if (config.EnforcementMode == EnforcementMode.None)
            {
                config.Enabled = false;
            }

            if (config.EnforcementMode == EnforcementMode.WebOnly)
            {
                config.EnableServerFallback = false;
            }
            else if (config.EnforcementMode == EnforcementMode.ServerFallback)
            {
                config.EnableServerFallback = true;

                if (config.ServerFallbackEpisodeThreshold > 0)
                {
                    config.EnableEpisodeCheck = true;
                    config.EpisodeThreshold = Math.Max(1, config.ServerFallbackEpisodeThreshold);
                }
                else
                {
                    config.EnableEpisodeCheck = false;
                }

                if (config.ServerFallbackMinutesThreshold > 0)
                {
                    config.EnableTimerCheck = true;
                    config.MinutesThreshold = Math.Max(1, config.ServerFallbackMinutesThreshold);
                }
                else
                {
                    config.EnableTimerCheck = false;
                }
            }

            // Ensure legacy persisted configs that lacked v3 booleans remain operable.
            if (!config.EnableEpisodeCheck && !config.EnableTimerCheck)
            {
                config.EnableEpisodeCheck = true;
                config.EnableTimerCheck = true;
            }

            config.SchemaVersion = CurrentSchemaVersion;
            migrated = true;
        }

        return migrated;
    }

    private static void Validate(PluginConfig config)
    {
        if (!config.EnableEpisodeCheck && !config.EnableTimerCheck)
        {
            throw new ArgumentOutOfRangeException(nameof(config.EnableEpisodeCheck), "At least one threshold check must be enabled.");
        }

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
        if (!Enum.IsDefined(config.MinimumLogLevel)) throw new ArgumentOutOfRangeException(nameof(config.MinimumLogLevel));
        if (config.SchemaVersion < 1) throw new ArgumentOutOfRangeException(nameof(config.SchemaVersion));
    }
}

