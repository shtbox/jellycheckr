using Jellycheckr.Contracts;
using Jellycheckr.Server.Models;

namespace Jellycheckr.Server.Services;

public interface IServerFallbackDecisionEngine
{
    ServerFallbackDecision Evaluate(SessionState state, EffectiveConfigResponse config, DateTimeOffset nowUtc);
}

public sealed class ServerFallbackDecisionEngine : IServerFallbackDecisionEngine
{
    public ServerFallbackDecision Evaluate(SessionState state, EffectiveConfigResponse config, DateTimeOffset nowUtc)
    {
        if (!config.Enabled)
        {
            return ServerFallbackDecision.Skip("disabled");
        }

        if (config.EnforcementMode != EnforcementMode.ServerFallback)
        {
            return ServerFallbackDecision.Skip("mode_not_server_fallback");
        }

        if (state.FallbackPhase == ServerFallbackPhase.PauseGracePending)
        {
            return ServerFallbackDecision.Skip("pause_grace_pending");
        }

        if (state.NextEligiblePromptUtc > nowUtc)
        {
            return ServerFallbackDecision.Skip("cooldown");
        }

        if (string.IsNullOrWhiteSpace(state.CurrentItemId))
        {
            return ServerFallbackDecision.Skip("no_current_item");
        }

        if (state.IsPaused == true)
        {
            return ServerFallbackDecision.Skip("paused");
        }

        // Developer mode provides a fast-path trigger for native-client fallback testing.
        // This mirrors the web quick-cycle intent without requiring episode/time/inactivity thresholds.
        if (config.DeveloperMode && config.DeveloperPromptAfterSeconds > 0)
        {
            var developerPlaybackSeconds = TimeSpan.FromTicks(Math.Max(0, state.ServerFallbackPlaybackTicksSinceReset)).TotalSeconds;
            if (developerPlaybackSeconds >= config.DeveloperPromptAfterSeconds)
            {
                return new ServerFallbackDecision(
                    true,
                    $"developer_mode_after_{config.DeveloperPromptAfterSeconds}s",
                    developerPlaybackSeconds / 60d,
                    state.ServerFallbackEpisodeTransitionsSinceReset,
                    0);
            }
        }

        var episodeThresholdEnabled = config.ServerFallbackEpisodeThreshold > 0;
        var minutesThresholdEnabled = config.ServerFallbackMinutesThreshold > 0;
        if (!episodeThresholdEnabled && !minutesThresholdEnabled)
        {
            return ServerFallbackDecision.Skip("thresholds_disabled");
        }

        var episodesReached = episodeThresholdEnabled
            && state.ServerFallbackEpisodeTransitionsSinceReset >= config.ServerFallbackEpisodeThreshold;
        var minutesPlayed = TimeSpan.FromTicks(Math.Max(0, state.ServerFallbackPlaybackTicksSinceReset)).TotalMinutes;
        var minutesReached = minutesThresholdEnabled && minutesPlayed >= config.ServerFallbackMinutesThreshold;

        var thresholdMet = config.ServerFallbackTriggerMode switch
        {
            ServerFallbackTriggerMode.All => (!episodeThresholdEnabled || episodesReached) && (!minutesThresholdEnabled || minutesReached),
            _ => (episodeThresholdEnabled && episodesReached) || (minutesThresholdEnabled && minutesReached)
        };

        if (!thresholdMet)
        {
            return ServerFallbackDecision.Skip(
                "thresholds_not_met",
                minutesPlayed,
                state.ServerFallbackEpisodeTransitionsSinceReset,
                0);
        }

        var activityAnchor = state.LastInferredActivityUtc != DateTimeOffset.MinValue
            ? state.LastInferredActivityUtc
            : (state.LastSeenUtc != DateTimeOffset.MinValue ? state.LastSeenUtc : nowUtc);
        var inactivityMinutes = Math.Max(0, (nowUtc - activityAnchor).TotalMinutes);
        if (inactivityMinutes < config.ServerFallbackInactivityMinutes)
        {
            return ServerFallbackDecision.Skip(
                "inactivity_not_met",
                minutesPlayed,
                state.ServerFallbackEpisodeTransitionsSinceReset,
                inactivityMinutes);
        }

        var reason = $"threshold={config.ServerFallbackTriggerMode} inactivity={inactivityMinutes:F1}m";
        return new ServerFallbackDecision(true, reason, minutesPlayed, state.ServerFallbackEpisodeTransitionsSinceReset, inactivityMinutes);
    }
}

public readonly record struct ServerFallbackDecision(
    bool ShouldTrigger,
    string Reason,
    double PlaybackMinutesSinceReset,
    int EpisodeTransitionsSinceReset,
    double InactivityMinutes)
{
    public static ServerFallbackDecision Skip(
        string reason,
        double playbackMinutesSinceReset = 0,
        int episodeTransitionsSinceReset = 0,
        double inactivityMinutes = 0)
        => new(false, reason, playbackMinutesSinceReset, episodeTransitionsSinceReset, inactivityMinutes);
}
