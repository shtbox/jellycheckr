using Jellycheckr.Server.Infrastructure;
using Jellycheckr.Server.Models;
using Microsoft.Extensions.Logging;

namespace Jellycheckr.Server.Services;

public interface ISessionStateObserver
{
    void ObserveSession(SessionState state, ServerObservedSessionSnapshot snapshot, DateTimeOffset nowUtc);
}

public sealed class SessionStateObserver : ISessionStateObserver
{
    private static readonly TimeSpan SeekDeltaTolerance = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan MinimumSeekSignal = TimeSpan.FromSeconds(15);

    private readonly ILogger<SessionStateObserver> _logger;

    public SessionStateObserver(ILogger<SessionStateObserver> logger)
    {
        _logger = logger;
    }

    public void ObserveSession(SessionState state, ServerObservedSessionSnapshot snapshot, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(snapshot);

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

        if (previousPaused == true && snapshot.IsPaused == false)
        {
            MarkInferredActivity(state, nowUtc, "resume_signal");
        }

        state.IsPaused = snapshot.IsPaused;

        var hasCurrentItem = !string.IsNullOrWhiteSpace(snapshot.ItemId);
        if (!hasCurrentItem)
        {
            if (!string.IsNullOrWhiteSpace(state.CurrentItemId))
            {
                state.PreviousItemId = state.CurrentItemId;
            }

            state.CurrentItemId = null;
            state.CurrentItemName = null;
            state.LastObservedPositionTicks = null;
            state.LastPlaybackProgressObservedUtc = nowUtc;
            state.LastObservedLastActivityDateUtc = snapshot.LastActivityUtc;
            state.LastObservedLastPlaybackCheckInUtc = snapshot.LastPlaybackCheckInUtc;
            state.LastObservedLastPausedDateUtc = snapshot.LastPausedUtc;
            return;
        }

        var comparisonItemId = !string.IsNullOrWhiteSpace(previousItemId)
            ? previousItemId
            : state.PreviousItemId;
        var itemChanged = !string.IsNullOrWhiteSpace(comparisonItemId)
            && !string.Equals(comparisonItemId, snapshot.ItemId, StringComparison.OrdinalIgnoreCase);

        if (itemChanged)
        {
            state.PreviousItemId = comparisonItemId;
            state.ServerFallbackEpisodeTransitionsSinceReset = Math.Max(0, state.ServerFallbackEpisodeTransitionsSinceReset) + 1;
            _logger.LogJellycheckrTrace(
                "Detected playback item transition session={SessionId} fromItem={FromItem} toItem={ToItem} transitionsSinceReset={Transitions}",
                state.SessionId,
                comparisonItemId,
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

    private static bool IsLikelyServerIssuedPauseObservation(SessionState state, DateTimeOffset? observedPauseUtc)
    {
        if (state.FallbackPhase != ServerFallbackPhase.PauseGracePending || state.PauseIssuedUtc is null || !observedPauseUtc.HasValue)
        {
            return false;
        }

        var delta = observedPauseUtc.Value - state.PauseIssuedUtc.Value;
        return delta >= TimeSpan.FromSeconds(-5) && delta <= TimeSpan.FromMinutes(5);
    }
}
