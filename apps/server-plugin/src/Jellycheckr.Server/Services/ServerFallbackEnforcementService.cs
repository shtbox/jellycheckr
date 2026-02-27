using Jellycheckr.Contracts;
using Jellycheckr.Server.Infrastructure;
using Jellycheckr.Server.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellycheckr.Server.Services;

public sealed class ServerFallbackEnforcementService : BackgroundService
{
    private static readonly TimeSpan LoopDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StaleStateTtl = TimeSpan.FromHours(6);
    private static readonly TimeSpan MissingSessionGrace = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan SeekDeltaTolerance = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan MinimumSeekSignal = TimeSpan.FromSeconds(15);

    private readonly ISessionStateStore _sessionStateStore;
    private readonly IConfigService _configService;
    private readonly IServerFallbackSessionSnapshotProvider _sessionSnapshotProvider;
    private readonly IServerFallbackDecisionEngine _decisionEngine;
    private readonly IJellyfinSessionCommandDispatcher _commandDispatcher;
    private readonly IWebUiInjectionState _webUiInjectionState;
    private readonly IClock _clock;
    private readonly ILogger<ServerFallbackEnforcementService> _logger;

    public ServerFallbackEnforcementService(
        ISessionStateStore sessionStateStore,
        IConfigService configService,
        IServerFallbackSessionSnapshotProvider sessionSnapshotProvider,
        IServerFallbackDecisionEngine decisionEngine,
        IJellyfinSessionCommandDispatcher commandDispatcher,
        IWebUiInjectionState webUiInjectionState,
        IClock clock,
        ILogger<ServerFallbackEnforcementService> logger)
    {
        _sessionStateStore = sessionStateStore;
        _configService = configService;
        _sessionSnapshotProvider = sessionSnapshotProvider;
        _decisionEngine = decisionEngine;
        _commandDispatcher = commandDispatcher;
        _webUiInjectionState = webUiInjectionState;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExecuteTickAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _logger.LogJellycheckrError(ex, "[Jellycheckr] Unhandled error in server fallback enforcement loop.");
            }

            await Task.Delay(LoopDelay, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task ExecuteTickAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var config = _configService.GetEffectiveConfig(null);
        _logger.LogJellycheckrTrace(
            "Server fallback loop tick nowUtc={NowUtc} config={@EffectiveConfig}",
            now,
            config);

        if (!config.Enabled || !config.EnableServerFallback)
        {
            _logger.LogJellycheckrTrace(
                "Skipping fallback enforcement because enabled={Enabled} fallbackEnabled={FallbackEnabled}",
                config.Enabled,
                config.EnableServerFallback);
            _sessionStateStore.PruneOlderThan(now.Subtract(StaleStateTtl));
            return;
        }

        ObserveCurrentSessions(now);
        await EvaluateSessionsAsync(now, config, cancellationToken).ConfigureAwait(false);
        _sessionStateStore.PruneOlderThan(now.Subtract(StaleStateTtl));
    }

    private void ObserveCurrentSessions(DateTimeOffset nowUtc)
    {
        var snapshots = _sessionSnapshotProvider.GetCurrentSessions();
        _logger.LogJellycheckrTrace(
            "Observing Jellyfin sessions count={Count}",
            snapshots.Count);

        foreach (var snapshot in snapshots)
        {
            if (string.IsNullOrWhiteSpace(snapshot.SessionId))
            {
                continue;
            }

            var state = _sessionStateStore.GetOrCreate(snapshot.SessionId);
            ObserveSession(state, snapshot, nowUtc);
        }
    }

    private void ObserveSession(
        SessionState state,
        ServerObservedSessionSnapshot snapshot,
        DateTimeOffset nowUtc)
    {
        var previousItemId = state.CurrentItemId;
        var previousPositionTicks = state.LastObservedPositionTicks;
        var previousProgressObservedUtc = state.LastPlaybackProgressObservedUtc;
        var previousPaused = state.IsPaused;

        state.UserId = snapshot.UserId;
        state.UserName = snapshot.UserName;
        state.ClientName = snapshot.ClientName;
        state.DeviceName = snapshot.DeviceName;
        state.DeviceId = snapshot.DeviceId;
        state.LastSeenUtc = nowUtc;

        if (HasAdvanced(state.LastObservedLastPausedDateUtc, snapshot.LastPausedUtc)
            && !IsLikelyServerIssuedPauseObservation(state, snapshot.LastPausedUtc))
        {
            MarkInferredActivity(state, snapshot.LastPausedUtc ?? nowUtc, "pause_signal");
        }

        if (HasMeaningfulLastActivityChange(state, snapshot)
            && !ShouldSuppressPassiveActivityDuringPauseGrace(state, snapshot))
        {
            MarkInferredActivity(state, snapshot.LastActivityUtc ?? nowUtc, "last_activity");
        }

        if (previousPaused == true && snapshot.IsPaused == false)
        {
            MarkInferredActivity(state, nowUtc, "resume_signal");
        }

        state.IsPaused = snapshot.IsPaused;

        var hasCurrentItem = !string.IsNullOrWhiteSpace(snapshot.ItemId);
        if (!hasCurrentItem)
        {
            state.PreviousItemId = state.CurrentItemId;
            state.CurrentItemId = null;
            state.CurrentItemName = null;
            state.LastObservedPositionTicks = null;
            state.LastPlaybackProgressObservedUtc = nowUtc;
            state.LastObservedLastActivityDateUtc = snapshot.LastActivityUtc;
            state.LastObservedLastPlaybackCheckInUtc = snapshot.LastPlaybackCheckInUtc;
            state.LastObservedLastPausedDateUtc = snapshot.LastPausedUtc;
            return;
        }

        var itemChanged = !string.IsNullOrWhiteSpace(previousItemId)
            && !string.Equals(previousItemId, snapshot.ItemId, StringComparison.OrdinalIgnoreCase);

        if (itemChanged)
        {
            state.PreviousItemId = previousItemId;
            state.ServerFallbackEpisodeTransitionsSinceReset = Math.Max(0, state.ServerFallbackEpisodeTransitionsSinceReset) + 1;
            _logger.LogJellycheckrTrace(
                "Detected playback item transition session={SessionId} fromItem={FromItem} toItem={ToItem} transitionsSinceReset={Transitions}",
                state.SessionId,
                previousItemId,
                snapshot.ItemId,
                state.ServerFallbackEpisodeTransitionsSinceReset);
        }

        state.CurrentItemId = snapshot.ItemId;
        state.CurrentItemName = snapshot.ItemName;

        if (state.LastInferredActivityUtc == DateTimeOffset.MinValue)
        {
            state.LastInferredActivityUtc = nowUtc;
        }

        if (previousPositionTicks.HasValue && snapshot.PositionTicks.HasValue && previousProgressObservedUtc.HasValue)
        {
            var elapsedTicks = Math.Max(0, (nowUtc - previousProgressObservedUtc.Value).Ticks);
            var positionDeltaTicks = snapshot.PositionTicks.Value - previousPositionTicks.Value;

            if (!itemChanged && elapsedTicks > 0 && positionDeltaTicks > 0 && previousPaused != true && snapshot.IsPaused != true)
            {
                AddPlaybackTicks(state, Math.Min(elapsedTicks, positionDeltaTicks));
            }

            if (!itemChanged && elapsedTicks > 0)
            {
                var expectedTicks = elapsedTicks;
                var deltaDifference = Math.Abs(positionDeltaTicks - expectedTicks);
                if (deltaDifference >= SeekDeltaTolerance.Ticks && Math.Abs(positionDeltaTicks) >= MinimumSeekSignal.Ticks)
                {
                    MarkInferredActivity(state, nowUtc, "seek_or_jump");
                }
            }
        }
        else if (!itemChanged && previousProgressObservedUtc.HasValue && previousPaused != true && snapshot.IsPaused != true)
        {
            var elapsedTicks = Math.Max(0, (nowUtc - previousProgressObservedUtc.Value).Ticks);
            if (elapsedTicks > 0 && HasAdvanced(state.LastObservedLastPlaybackCheckInUtc, snapshot.LastPlaybackCheckInUtc))
            {
                AddPlaybackTicks(state, elapsedTicks);
            }
        }

        state.LastObservedPositionTicks = snapshot.PositionTicks;
        state.LastPlaybackProgressObservedUtc = nowUtc;
        state.LastObservedLastActivityDateUtc = snapshot.LastActivityUtc;
        state.LastObservedLastPlaybackCheckInUtc = snapshot.LastPlaybackCheckInUtc;
        state.LastObservedLastPausedDateUtc = snapshot.LastPausedUtc;
    }

    private async Task EvaluateSessionsAsync(DateTimeOffset nowUtc, EffectiveConfigResponse config, CancellationToken cancellationToken)
    {
        var states = _sessionStateStore.Snapshot();
        _logger.LogJellycheckrTrace(
            "Evaluating fallback enforcement snapshotCount={Count} nowUtc={NowUtc}",
            states.Count,
            nowUtc);

        foreach (var state in states)
        {
            if (state.FallbackPhase == ServerFallbackPhase.PauseGracePending)
            {
                await HandlePauseGracePendingAsync(state, config, nowUtc, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (state.LastSeenUtc == DateTimeOffset.MinValue || nowUtc - state.LastSeenUtc > MissingSessionGrace)
            {
                continue;
            }

            if (IsLikelyWebClient(state) && _webUiInjectionState.IsRegistered)
            {
                var webSkipDecision = BuildSkipDecision(state, nowUtc, "web_client_excluded_injection_registered");
                LogDecisionIfChanged(state, webSkipDecision, config, nowUtc);
                continue;
            }

            var decision = _decisionEngine.Evaluate(state, config, nowUtc);
            _logger.LogJellycheckrTrace(
                "Fallback decision session={SessionId} trigger={Trigger} reason={Reason} minutes={Minutes:F2} transitions={Transitions} inactivityMinutes={Inactivity:F2}",
                state.SessionId,
                decision.ShouldTrigger,
                decision.Reason,
                decision.PlaybackMinutesSinceReset,
                decision.EpisodeTransitionsSinceReset,
                decision.InactivityMinutes);
            LogDecisionIfChanged(state, decision, config, nowUtc);

            if (!decision.ShouldTrigger)
            {
                continue;
            }

            await TriggerFallbackAsync(state, config, nowUtc, decision, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task TriggerFallbackAsync(
        SessionState state,
        EffectiveConfigResponse config,
        DateTimeOffset nowUtc,
        ServerFallbackDecision decision,
        CancellationToken cancellationToken)
    {
        _logger.LogJellycheckrInformation(
            "[Jellycheckr] Server fallback trigger session={SessionId} client={Client} device={Device} item={ItemId} reason={Reason}.",
            state.SessionId,
            state.ClientName ?? "(unknown)",
            state.DeviceName ?? "(unknown)",
            state.CurrentItemId ?? "(none)",
            decision.Reason);

        if (config.ServerFallbackDryRun)
        {
            state.LastFallbackAction = "dry_run";
            state.LastFallbackActionResult = decision.Reason;
            _logger.LogJellycheckrInformation(
                "[Jellycheckr] Fallback action decision session={SessionId} action=dry_run reason={Reason} playbackMinutes={PlaybackMinutes:F2} episodeTransitions={EpisodeTransitions} inactivityMinutes={InactivityMinutes:F2}.",
                state.SessionId,
                decision.Reason,
                decision.PlaybackMinutesSinceReset,
                decision.EpisodeTransitionsSinceReset,
                decision.InactivityMinutes);
            ApplyFallbackResetAndCooldown(state, nowUtc, config.CooldownMinutes);
            return;
        }

        if (config.ServerFallbackSendMessageBeforePause && !string.IsNullOrWhiteSpace(config.ServerFallbackClientMessage))
        {
            var messageSent = await _commandDispatcher.TrySendMessageAsync(
                state.SessionId,
                state.UserId,
                config.ServerFallbackClientMessage!,
                cancellationToken).ConfigureAwait(false);
            state.LastFallbackAction = "message";
            state.LastFallbackActionResult = messageSent ? "sent" : "failed";
            _logger.LogJellycheckrInformation(
                "[Jellycheckr] Fallback action decision session={SessionId} action=message result={Result}.",
                state.SessionId,
                state.LastFallbackActionResult);
        }

        if (!config.ServerFallbackPauseBeforeStop)
        {
            var stopSent = await _commandDispatcher.TrySendStopAsync(state.SessionId, state.UserId, cancellationToken).ConfigureAwait(false);
            state.LastFallbackAction = "stop";
            state.LastFallbackActionResult = stopSent ? "sent" : "failed";
            _logger.LogJellycheckrInformation(
                "[Jellycheckr] Fallback action decision session={SessionId} action=stop result={Result} reason={Reason}.",
                state.SessionId,
                state.LastFallbackActionResult,
                decision.Reason);
            ApplyFallbackResetAndCooldown(state, nowUtc, config.CooldownMinutes, applyCooldown: !stopSent);
            return;
        }

        var pauseSent = await _commandDispatcher.TrySendPauseAsync(state.SessionId, state.UserId, cancellationToken).ConfigureAwait(false);
        state.FallbackPhase = ServerFallbackPhase.PauseGracePending;
        state.PauseIssuedUtc = nowUtc;
        state.PauseGraceDeadlineUtc = nowUtc.AddSeconds(Math.Max(5, config.ServerFallbackPauseGraceSeconds));
        state.LastFallbackAction = "pause";
        state.LastFallbackActionResult = pauseSent ? "sent" : "failed";

        _logger.LogJellycheckrInformation(
            "[Jellycheckr] Server fallback pause initiated for session={SessionId}; graceUntilUtc={GraceUntilUtc} pauseSent={PauseSent}.",
            state.SessionId,
            state.PauseGraceDeadlineUtc,
            pauseSent);
    }

    private async Task HandlePauseGracePendingAsync(
        SessionState state,
        EffectiveConfigResponse config,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (state.PauseGraceDeadlineUtc is null)
        {
            state.FallbackPhase = ServerFallbackPhase.Monitoring;
            state.PauseIssuedUtc = null;
            return;
        }

        var sessionMissing = state.LastSeenUtc == DateTimeOffset.MinValue || nowUtc - state.LastSeenUtc > MissingSessionGrace;

        if (string.IsNullOrWhiteSpace(state.CurrentItemId) && !sessionMissing)
        {
            _logger.LogJellycheckrInformation(
                "[Jellycheckr] Clearing fallback pause grace for session={SessionId}; playback appears ended.",
                state.SessionId);
            ApplyFallbackResetAndCooldown(state, nowUtc, config.CooldownMinutes);
            return;
        }

        if (IsGraceResolvedByUserActivity(state))
        {
            _logger.LogJellycheckrInformation(
                "[Jellycheckr] Fallback pause grace resolved by activity for session={SessionId}; applying cooldown.",
                state.SessionId);
            state.LastFallbackAction = "resume";
            state.LastFallbackActionResult = "activity_detected";
            ApplyFallbackResetAndCooldown(state, nowUtc, config.CooldownMinutes);
            return;
        }

        if (nowUtc < state.PauseGraceDeadlineUtc.Value)
        {
            if (sessionMissing)
            {
                _logger.LogJellycheckrTrace(
                    "Pause grace pending session={SessionId} waiting despite missing snapshot lastSeenUtc={LastSeenUtc} graceUntilUtc={GraceUntilUtc}",
                    state.SessionId,
                    state.LastSeenUtc,
                    state.PauseGraceDeadlineUtc);
            }
            return;
        }

        if (sessionMissing)
        {
            _logger.LogJellycheckrWarning(
                "[Jellycheckr] Pause grace expired for session={SessionId} but session snapshot is missing; attempting stop using cached session id.",
                state.SessionId);
        }

        if (config.ServerFallbackDryRun)
        {
            state.LastFallbackAction = "dry_run_stop";
            state.LastFallbackActionResult = "deadline_elapsed";
            _logger.LogJellycheckrInformation(
                "[Jellycheckr] Fallback action decision session={SessionId} action=dry_run_stop reason={Reason}.",
                state.SessionId,
                state.LastFallbackActionResult);
            ApplyFallbackResetAndCooldown(state, nowUtc, config.CooldownMinutes);
            return;
        }

        var stopSent = await _commandDispatcher.TrySendStopAsync(state.SessionId, state.UserId, cancellationToken).ConfigureAwait(false);
        state.LastFallbackAction = "stop";
        state.LastFallbackActionResult = stopSent ? "sent_after_grace" : "failed_after_grace";
        _logger.LogJellycheckrInformation(
            "[Jellycheckr] Server fallback grace expired for session={SessionId}; stop command attempted result={Result}.",
            state.SessionId,
            state.LastFallbackActionResult);
        ApplyFallbackResetAndCooldown(state, nowUtc, config.CooldownMinutes, applyCooldown: !stopSent);
    }

    private void LogDecisionIfChanged(
        SessionState state,
        ServerFallbackDecision decision,
        EffectiveConfigResponse config,
        DateTimeOffset nowUtc)
    {
        var decisionKey = BuildDecisionKey(decision);
        if (string.Equals(state.LastFallbackDecisionKey, decisionKey, StringComparison.Ordinal))
        {
            return;
        }

        state.LastFallbackDecisionKey = decisionKey;
        state.LastFallbackDecisionLoggedUtc = nowUtc;

        var episodeGate = config.EnableEpisodeCheck
            ? $"{decision.EpisodeTransitionsSinceReset}/{config.EpisodeThreshold}"
            : "off";
        var minuteGate = config.EnableTimerCheck
            ? $"{decision.PlaybackMinutesSinceReset:F2}/{config.MinutesThreshold:F2}"
            : "off";
        var inactivityGate = $"{decision.InactivityMinutes:F2}/{config.ServerFallbackInactivityMinutes:F2}";

        _logger.LogJellycheckrInformation(
            "[Jellycheckr] Fallback decision session={SessionId} trigger={Trigger} reason={Reason} episodeGate={EpisodeGate} minuteGate={MinuteGate} inactivityGate={InactivityGate} phase={Phase}.",
            state.SessionId,
            decision.ShouldTrigger,
            decision.Reason,
            episodeGate,
            minuteGate,
            inactivityGate,
            state.FallbackPhase);
    }

    private static ServerFallbackDecision BuildSkipDecision(SessionState state, DateTimeOffset nowUtc, string reason)
    {
        var minutesPlayed = TimeSpan.FromTicks(Math.Max(0, state.ServerFallbackPlaybackTicksSinceReset)).TotalMinutes;
        var transitions = Math.Max(0, state.ServerFallbackEpisodeTransitionsSinceReset);
        var activityAnchor = state.LastInferredActivityUtc != DateTimeOffset.MinValue
            ? state.LastInferredActivityUtc
            : (state.LastSeenUtc != DateTimeOffset.MinValue ? state.LastSeenUtc : nowUtc);
        var inactivityMinutes = Math.Max(0, (nowUtc - activityAnchor).TotalMinutes);

        return ServerFallbackDecision.Skip(
            reason,
            minutesPlayed,
            transitions,
            inactivityMinutes);
    }

    private static string BuildDecisionKey(ServerFallbackDecision decision)
    {
        if (!decision.ShouldTrigger)
        {
            return $"skip:{decision.Reason}";
        }

        if (decision.Reason.StartsWith("developer_mode_after_", StringComparison.Ordinal))
        {
            return $"trigger:{decision.Reason}";
        }

        return "trigger:threshold_or_inactivity_met";
    }

    private static bool IsGraceResolvedByUserActivity(SessionState state)
    {
        if (state.PauseIssuedUtc is null)
        {
            return false;
        }

        if (state.LastInferredActivityUtc <= state.PauseIssuedUtc.Value)
        {
            return false;
        }

        if (IsLikelyFallbackPauseActivityMarker(state))
        {
            return false;
        }

        return state.IsPaused != true;
    }

    private static void ApplyFallbackResetAndCooldown(
        SessionState state,
        DateTimeOffset nowUtc,
        int cooldownMinutes,
        bool applyCooldown = true)
    {
        state.FallbackPhase = ServerFallbackPhase.Monitoring;
        state.PauseIssuedUtc = null;
        state.PauseGraceDeadlineUtc = null;
        state.ServerFallbackEpisodeTransitionsSinceReset = 0;
        state.ServerFallbackPlaybackTicksSinceReset = 0;
        state.NextEligiblePromptUtc = applyCooldown
            ? nowUtc.AddMinutes(Math.Max(0, cooldownMinutes))
            : DateTimeOffset.MinValue;
        state.LastAckUtc = nowUtc;
        state.LastInferredActivityUtc = nowUtc;
    }

    private static void MarkInferredActivity(SessionState state, DateTimeOffset atUtc, string reason)
    {
        if (atUtc <= DateTimeOffset.MinValue)
        {
            return;
        }

        if (atUtc > state.LastInferredActivityUtc)
        {
            state.LastInferredActivityUtc = atUtc;
            state.LastFallbackAction = "activity";
            state.LastFallbackActionResult = reason;
        }
    }

    private static void AddPlaybackTicks(SessionState state, long ticks)
    {
        if (ticks <= 0)
        {
            return;
        }

        var boundedTicks = Math.Min(ticks, TimeSpan.FromMinutes(5).Ticks);
        state.ServerFallbackPlaybackTicksSinceReset = checked(state.ServerFallbackPlaybackTicksSinceReset + boundedTicks);
    }

    private static bool HasAdvanced(DateTimeOffset? previous, DateTimeOffset? current)
    {
        return previous.HasValue && current.HasValue
            ? current.Value > previous.Value
            : !previous.HasValue && current.HasValue;
    }

    private static bool HasMeaningfulLastActivityChange(SessionState state, ServerObservedSessionSnapshot snapshot)
    {
        if (!HasAdvanced(state.LastObservedLastActivityDateUtc, snapshot.LastActivityUtc))
        {
            return false;
        }

        if (!snapshot.LastActivityUtc.HasValue || !snapshot.LastPlaybackCheckInUtc.HasValue)
        {
            return false;
        }

        if (!HasAdvanced(state.LastObservedLastPlaybackCheckInUtc, snapshot.LastPlaybackCheckInUtc))
        {
            return false;
        }

        return (snapshot.LastActivityUtc.Value - snapshot.LastPlaybackCheckInUtc.Value).Duration() > TimeSpan.FromSeconds(2);
    }

    private static bool ShouldSuppressPassiveActivityDuringPauseGrace(SessionState state, ServerObservedSessionSnapshot snapshot)
    {
        if (state.FallbackPhase != ServerFallbackPhase.PauseGracePending || state.PauseIssuedUtc is null)
        {
            return false;
        }

        // While waiting out the grace window, some clients emit LastActivity updates during normal playback
        // heartbeats (including when they ignore our pause command). Those must not count as user activity,
        // or the grace window is immediately "resolved" and we remain in a cooldown loop forever.
        return true;
    }

    private static bool IsLikelyServerIssuedPauseObservation(SessionState state, DateTimeOffset? observedPauseUtc)
    {
        if (state.FallbackPhase != ServerFallbackPhase.PauseGracePending || state.PauseIssuedUtc is null || !observedPauseUtc.HasValue)
        {
            return false;
        }

        // Allow for minor timestamp skew between command issue time and Jellyfin session metadata updates.
        var delta = observedPauseUtc.Value - state.PauseIssuedUtc.Value;
        return delta >= TimeSpan.FromSeconds(-5) && delta <= TimeSpan.FromMinutes(5);
    }

    private static bool IsLikelyFallbackPauseActivityMarker(SessionState state)
    {
        if (state.PauseIssuedUtc is null || !state.LastObservedLastPausedDateUtc.HasValue)
        {
            return false;
        }

        var pausedDelta = state.LastObservedLastPausedDateUtc.Value - state.PauseIssuedUtc.Value;
        if (pausedDelta < TimeSpan.FromSeconds(-5) || pausedDelta > TimeSpan.FromMinutes(5))
        {
            return false;
        }

        return (state.LastInferredActivityUtc - state.LastObservedLastPausedDateUtc.Value).Duration() <= TimeSpan.FromSeconds(2);
    }

    private static bool IsLikelyWebClient(SessionState state)
    {
        var client = state.ClientName ?? string.Empty;
        var device = state.DeviceName ?? string.Empty;
        return client.Contains("web", StringComparison.OrdinalIgnoreCase)
               || device.Contains("browser", StringComparison.OrdinalIgnoreCase);
    }
}

