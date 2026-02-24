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
    private readonly IClock _clock;
    private readonly ILogger<ServerFallbackEnforcementService> _logger;

    public ServerFallbackEnforcementService(
        ISessionStateStore sessionStateStore,
        IConfigService configService,
        IServerFallbackSessionSnapshotProvider sessionSnapshotProvider,
        IServerFallbackDecisionEngine decisionEngine,
        IJellyfinSessionCommandDispatcher commandDispatcher,
        IClock clock,
        ILogger<ServerFallbackEnforcementService> logger)
    {
        _sessionStateStore = sessionStateStore;
        _configService = configService;
        _sessionSnapshotProvider = sessionSnapshotProvider;
        _decisionEngine = decisionEngine;
        _commandDispatcher = commandDispatcher;
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
                _logger.LogError(ex, "[Jellycheckr] Unhandled error in server fallback enforcement loop.");
            }

            await Task.Delay(LoopDelay, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task ExecuteTickAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var config = _configService.GetEffectiveConfig(null);
        JellycheckrDiagnosticLogging.Verbose(
            _logger,
            config,
            "Server fallback loop tick nowUtc={NowUtc} config={@EffectiveConfig}",
            now,
            JellycheckrDiagnosticLogging.Describe(config));

        if (config.EnforcementMode != EnforcementMode.ServerFallback || !config.Enabled)
        {
            JellycheckrDiagnosticLogging.Verbose(
                _logger,
                config,
                "Skipping fallback enforcement because enabled={Enabled} mode={EnforcementMode}",
                config.Enabled,
                config.EnforcementMode);
            _sessionStateStore.PruneOlderThan(now.Subtract(StaleStateTtl));
            return;
        }

        ObserveCurrentSessions(now, config);
        await EvaluateSessionsAsync(now, config, cancellationToken).ConfigureAwait(false);
        _sessionStateStore.PruneOlderThan(now.Subtract(StaleStateTtl));
    }

    private void ObserveCurrentSessions(DateTimeOffset nowUtc, EffectiveConfigResponse config)
    {
        var snapshots = _sessionSnapshotProvider.GetCurrentSessions();
        JellycheckrDiagnosticLogging.Verbose(
            _logger,
            config,
            "Observing Jellyfin sessions count={Count}",
            snapshots.Count);

        foreach (var snapshot in snapshots)
        {
            if (string.IsNullOrWhiteSpace(snapshot.SessionId))
            {
                continue;
            }

            var state = _sessionStateStore.GetOrCreate(snapshot.SessionId);
            ObserveSession(state, snapshot, nowUtc, config);
        }
    }

    private void ObserveSession(
        SessionState state,
        ServerObservedSessionSnapshot snapshot,
        DateTimeOffset nowUtc,
        EffectiveConfigResponse config)
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
            JellycheckrDiagnosticLogging.Verbose(
                _logger,
                config,
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
        JellycheckrDiagnosticLogging.Verbose(
            _logger,
            config,
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

            if (IsLikelyWebClient(state))
            {
                continue;
            }

            var decision = _decisionEngine.Evaluate(state, config, nowUtc);
            JellycheckrDiagnosticLogging.Verbose(
                _logger,
                config,
                "Fallback decision session={SessionId} trigger={Trigger} reason={Reason} minutes={Minutes:F2} transitions={Transitions} inactivityMinutes={Inactivity:F2}",
                state.SessionId,
                decision.ShouldTrigger,
                decision.Reason,
                decision.PlaybackMinutesSinceReset,
                decision.EpisodeTransitionsSinceReset,
                decision.InactivityMinutes);

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
        _logger.LogInformation(
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
        }

        if (!config.ServerFallbackPauseBeforeStop)
        {
            var stopSent = await _commandDispatcher.TrySendStopAsync(state.SessionId, state.UserId, cancellationToken).ConfigureAwait(false);
            state.LastFallbackAction = "stop";
            state.LastFallbackActionResult = stopSent ? "sent" : "failed";
            ApplyFallbackResetAndCooldown(state, nowUtc, config.CooldownMinutes);
            return;
        }

        var pauseSent = await _commandDispatcher.TrySendPauseAsync(state.SessionId, state.UserId, cancellationToken).ConfigureAwait(false);
        state.FallbackPhase = ServerFallbackPhase.PauseGracePending;
        state.PauseIssuedUtc = nowUtc;
        state.PauseGraceDeadlineUtc = nowUtc.AddSeconds(Math.Max(5, config.ServerFallbackPauseGraceSeconds));
        state.LastFallbackAction = "pause";
        state.LastFallbackActionResult = pauseSent ? "sent" : "failed";

        _logger.LogInformation(
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
            _logger.LogInformation(
                "[Jellycheckr] Clearing fallback pause grace for session={SessionId}; playback appears ended.",
                state.SessionId);
            ApplyFallbackResetAndCooldown(state, nowUtc, config.CooldownMinutes);
            return;
        }

        if (IsGraceResolvedByUserActivity(state))
        {
            _logger.LogInformation(
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
                JellycheckrDiagnosticLogging.Verbose(
                    _logger,
                    config,
                    "Pause grace pending session={SessionId} waiting despite missing snapshot lastSeenUtc={LastSeenUtc} graceUntilUtc={GraceUntilUtc}",
                    state.SessionId,
                    state.LastSeenUtc,
                    state.PauseGraceDeadlineUtc);
            }
            return;
        }

        if (sessionMissing)
        {
            _logger.LogWarning(
                "[Jellycheckr] Pause grace expired for session={SessionId} but session snapshot is missing; attempting stop using cached session id.",
                state.SessionId);
        }

        if (config.ServerFallbackDryRun)
        {
            state.LastFallbackAction = "dry_run_stop";
            state.LastFallbackActionResult = "deadline_elapsed";
            ApplyFallbackResetAndCooldown(state, nowUtc, config.CooldownMinutes);
            return;
        }

        var stopSent = await _commandDispatcher.TrySendStopAsync(state.SessionId, state.UserId, cancellationToken).ConfigureAwait(false);
        state.LastFallbackAction = "stop";
        state.LastFallbackActionResult = stopSent ? "sent_after_grace" : "failed_after_grace";
        _logger.LogWarning(
            "[Jellycheckr] Server fallback grace expired for session={SessionId}; stop command attempted result={Result}.",
            state.SessionId,
            state.LastFallbackActionResult);
        ApplyFallbackResetAndCooldown(state, nowUtc, config.CooldownMinutes);
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

    private static void ApplyFallbackResetAndCooldown(SessionState state, DateTimeOffset nowUtc, int cooldownMinutes)
    {
        state.FallbackPhase = ServerFallbackPhase.Monitoring;
        state.PauseIssuedUtc = null;
        state.PauseGraceDeadlineUtc = null;
        state.ServerFallbackEpisodeTransitionsSinceReset = 0;
        state.ServerFallbackPlaybackTicksSinceReset = 0;
        state.NextEligiblePromptUtc = nowUtc.AddMinutes(Math.Max(0, cooldownMinutes));
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
            return true;
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
