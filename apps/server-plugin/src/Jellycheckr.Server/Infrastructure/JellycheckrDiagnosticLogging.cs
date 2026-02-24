using Jellycheckr.Contracts;
using Jellycheckr.Server.Models;
using Jellycheckr.Server.Services;
using Microsoft.Extensions.Logging;

namespace Jellycheckr.Server.Infrastructure;

public static class JellycheckrDiagnosticLogging
{
    public static bool IsVerboseEnabled(PluginConfig? config)
        => config?.DeveloperMode == true || config?.DebugLogging == true;

    public static bool IsVerboseEnabled(EffectiveConfigResponse? config)
        => config?.DeveloperMode == true || config?.DebugLogging == true;

    public static bool IsVerboseEnabled(IConfigService configService)
    {
        try
        {
            return IsVerboseEnabled(configService.GetAdminConfig());
        }
        catch
        {
            return false;
        }
    }

    public static void Verbose(ILogger logger, IConfigService configService, string message, params object?[] args)
    {
        if (IsVerboseEnabled(configService))
        {
            logger.LogInformation("[Jellycheckr][Trace] " + message, args);
        }
    }

    public static void Verbose(ILogger logger, PluginConfig? config, string message, params object?[] args)
    {
        if (IsVerboseEnabled(config))
        {
            logger.LogInformation("[Jellycheckr][Trace] " + message, args);
        }
    }

    public static void Verbose(ILogger logger, EffectiveConfigResponse? config, string message, params object?[] args)
    {
        if (IsVerboseEnabled(config))
        {
            logger.LogInformation("[Jellycheckr][Trace] " + message, args);
        }
    }

    public static object Describe(PluginConfig config)
        => new
        {
            config.Enabled,
            config.EpisodeThreshold,
            config.MinutesThreshold,
            config.InteractionQuietSeconds,
            config.PromptTimeoutSeconds,
            config.CooldownMinutes,
            config.EnforcementMode,
            config.ServerFallbackEpisodeThreshold,
            config.ServerFallbackMinutesThreshold,
            config.ServerFallbackTriggerMode,
            config.ServerFallbackInactivityMinutes,
            config.ServerFallbackPauseBeforeStop,
            config.ServerFallbackPauseGraceSeconds,
            config.ServerFallbackSendMessageBeforePause,
            config.ServerFallbackClientMessage,
            config.ServerFallbackDryRun,
            config.DebugLogging,
            config.DeveloperMode,
            config.DeveloperPromptAfterSeconds,
            config.SchemaVersion
        };

    public static object Describe(EffectiveConfigResponse config)
        => new
        {
            config.Enabled,
            config.EpisodeThreshold,
            config.MinutesThreshold,
            config.InteractionQuietSeconds,
            config.PromptTimeoutSeconds,
            config.CooldownMinutes,
            config.EnforcementMode,
            config.ServerFallbackEpisodeThreshold,
            config.ServerFallbackMinutesThreshold,
            config.ServerFallbackTriggerMode,
            config.ServerFallbackInactivityMinutes,
            config.ServerFallbackPauseBeforeStop,
            config.ServerFallbackPauseGraceSeconds,
            config.ServerFallbackSendMessageBeforePause,
            config.ServerFallbackClientMessage,
            config.ServerFallbackDryRun,
            config.DebugLogging,
            config.DeveloperMode,
            config.DeveloperPromptAfterSeconds,
            config.Version,
            config.SchemaVersion
        };

    public static object Describe(SessionState state)
        => new
        {
            state.SessionId,
            state.LastAckUtc,
            state.LastInteractionUtc,
            state.PromptActive,
            state.PromptDeadlineUtc,
            state.LastItemId,
            state.ConsecutiveEpisodesSinceAck,
            state.NextEligiblePromptUtc,
            state.UserId,
            state.UserName,
            state.ClientName,
            state.DeviceName,
            state.DeviceId,
            state.CurrentItemId,
            state.CurrentItemName,
            state.PreviousItemId,
            state.LastSeenUtc,
            state.LastObservedPositionTicks,
            state.LastPlaybackProgressObservedUtc,
            state.LastObservedLastActivityDateUtc,
            state.LastObservedLastPlaybackCheckInUtc,
            state.LastObservedLastPausedDateUtc,
            state.LastInferredActivityUtc,
            state.ServerFallbackEpisodeTransitionsSinceReset,
            state.ServerFallbackPlaybackTicksSinceReset,
            state.IsPaused,
            state.FallbackPhase,
            state.PauseIssuedUtc,
            state.PauseGraceDeadlineUtc,
            state.LastFallbackAction,
            state.LastFallbackActionResult
        };
}
