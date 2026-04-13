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
        var previousItemType = state.CurrentItemType;
        var previousSeriesId = state.CurrentSeriesId;
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
            ObserveNoCurrentItem(state, snapshot, nowUtc);
            return;
        }

        var resumedAfterGap = state.NoCurrentItemSinceUtc.HasValue
            && nowUtc - state.NoCurrentItemSinceUtc.Value >= SessionMonitoringPolicy.NoCurrentItemResetGrace;
        if (resumedAfterGap)
        {
            PrepareForFreshPlaybackSession(state, nowUtc);
        }

        state.NoCurrentItemSinceUtc = null;

        var comparisonItemId = !string.IsNullOrWhiteSpace(previousItemId)
            ? previousItemId
            : state.PreviousItemId;
        var comparisonItemType = !string.IsNullOrWhiteSpace(previousItemId)
            ? previousItemType
            : state.PreviousItemType;
        var comparisonSeriesId = !string.IsNullOrWhiteSpace(previousItemId)
            ? previousSeriesId
            : state.PreviousSeriesId;
        var itemChanged = !string.IsNullOrWhiteSpace(comparisonItemId)
            && !string.Equals(comparisonItemId, snapshot.ItemId, StringComparison.OrdinalIgnoreCase);

        if (itemChanged)
        {
            state.PreviousItemId = comparisonItemId;
            state.PreviousItemType = comparisonItemType;
            state.PreviousSeriesId = comparisonSeriesId;

            if (ShouldCountEpisodeTransition(comparisonItemType, comparisonSeriesId, snapshot.ItemType, snapshot.SeriesId))
            {
                state.ServerFallbackEpisodeTransitionsSinceReset = Math.Max(0, state.ServerFallbackEpisodeTransitionsSinceReset) + 1;
                _logger.LogJellycheckrTrace(
                    "Detected episode transition session={SessionId} fromItem={FromItem} toItem={ToItem} transitionsSinceReset={Transitions}",
                    state.SessionId,
                    comparisonItemId,
                    snapshot.ItemId,
                    state.ServerFallbackEpisodeTransitionsSinceReset);
            }
            else
            {
                _logger.LogJellycheckrTrace(
                    "Ignoring non-episode transition session={SessionId} fromItem={FromItem} toItem={ToItem} fromType={FromType} toType={ToType} fromSeries={FromSeries} toSeries={ToSeries} reason={Reason}",
                    state.SessionId,
                    comparisonItemId,
                    snapshot.ItemId,
                    comparisonItemType ?? "(unknown)",
                    snapshot.ItemType ?? "(unknown)",
                    comparisonSeriesId ?? "(none)",
                    snapshot.SeriesId ?? "(none)",
                    DescribeIgnoredTransitionReason(comparisonItemType, comparisonSeriesId, snapshot.ItemType, snapshot.SeriesId));
            }
        }

        state.CurrentItemId = snapshot.ItemId;
        state.CurrentItemName = snapshot.ItemName;
        state.CurrentItemType = snapshot.ItemType;
        state.CurrentSeriesId = snapshot.SeriesId;

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

    private void ObserveNoCurrentItem(SessionState state, ServerObservedSessionSnapshot snapshot, DateTimeOffset nowUtc)
    {
        if (!state.NoCurrentItemSinceUtc.HasValue)
        {
            state.NoCurrentItemSinceUtc = nowUtc;
        }

        if (!string.IsNullOrWhiteSpace(state.CurrentItemId))
        {
            state.PreviousItemId = state.CurrentItemId;
            state.PreviousItemType = state.CurrentItemType;
            state.PreviousSeriesId = state.CurrentSeriesId;
        }

        state.CurrentItemId = null;
        state.CurrentItemName = null;
        state.CurrentItemType = null;
        state.CurrentSeriesId = null;
        state.LastObservedPositionTicks = null;
        state.LastPlaybackProgressObservedUtc = nowUtc;
        state.LastObservedLastActivityDateUtc = snapshot.LastActivityUtc;
        state.LastObservedLastPlaybackCheckInUtc = snapshot.LastPlaybackCheckInUtc;
        state.LastObservedLastPausedDateUtc = snapshot.LastPausedUtc;

        if (nowUtc - state.NoCurrentItemSinceUtc.Value >= SessionMonitoringPolicy.NoCurrentItemResetGrace)
        {
            ResetPlaybackWindowAfterEndedPlayback(state, nowUtc);
        }
    }

    private void PrepareForFreshPlaybackSession(SessionState state, DateTimeOffset nowUtc)
    {
        state.CurrentItemId = null;
        state.CurrentItemName = null;
        state.CurrentItemType = null;
        state.CurrentSeriesId = null;
        state.PreviousItemId = null;
        state.PreviousItemType = null;
        state.PreviousSeriesId = null;
        state.ServerFallbackEpisodeTransitionsSinceReset = 0;
        state.ServerFallbackPlaybackTicksSinceReset = 0;
        state.LastObservedPositionTicks = null;
        state.LastPlaybackProgressObservedUtc = null;
        state.LastInferredActivityUtc = nowUtc;
        state.PromptActive = false;
        state.PromptDeadlineUtc = null;
        state.LastFallbackDecisionKey = null;
        state.LastFallbackDecisionLoggedUtc = null;
        state.LastFallbackAction = "playback_reset";
        state.LastFallbackActionResult = "playback_resumed_after_gap";

        _logger.LogJellycheckrInformation(
            "[Jellycheckr] Treating playback as a fresh session after a sustained no-item gap session={SessionId}.",
            state.SessionId);
    }

    private void ResetPlaybackWindowAfterEndedPlayback(SessionState state, DateTimeOffset nowUtc)
    {
        var hadPlaybackWindow =
            !string.IsNullOrWhiteSpace(state.PreviousItemId)
            || state.ServerFallbackEpisodeTransitionsSinceReset > 0
            || state.ServerFallbackPlaybackTicksSinceReset > 0
            || state.PromptActive
            || state.PromptDeadlineUtc.HasValue
            || state.LastObservedPositionTicks.HasValue;

        if (!hadPlaybackWindow)
        {
            return;
        }

        state.PreviousItemId = null;
        state.PreviousItemType = null;
        state.PreviousSeriesId = null;
        state.ServerFallbackEpisodeTransitionsSinceReset = 0;
        state.ServerFallbackPlaybackTicksSinceReset = 0;
        state.PromptActive = false;
        state.PromptDeadlineUtc = null;
        state.LastObservedPositionTicks = null;
        state.LastPlaybackProgressObservedUtc = null;
        state.LastFallbackDecisionKey = null;
        state.LastFallbackDecisionLoggedUtc = null;
        state.LastFallbackAction = "playback_reset";
        state.LastFallbackActionResult = "no_current_item_gap";

        _logger.LogJellycheckrInformation(
            "[Jellycheckr] Resetting playback window after sustained no-item gap session={SessionId} gapSeconds={GapSeconds:F1}.",
            state.SessionId,
            (nowUtc - state.NoCurrentItemSinceUtc!.Value).TotalSeconds);
    }

    private static bool ShouldCountEpisodeTransition(
        string? previousItemType,
        string? previousSeriesId,
        string? nextItemType,
        string? nextSeriesId)
    {
        return IsEpisode(previousItemType)
            && IsEpisode(nextItemType)
            && !string.IsNullOrWhiteSpace(previousSeriesId)
            && string.Equals(previousSeriesId, nextSeriesId, StringComparison.OrdinalIgnoreCase);
    }

    private static string DescribeIgnoredTransitionReason(
        string? previousItemType,
        string? previousSeriesId,
        string? nextItemType,
        string? nextSeriesId)
    {
        if (!IsEpisode(previousItemType) || !IsEpisode(nextItemType))
        {
            return "non_episode_type";
        }

        if (string.IsNullOrWhiteSpace(previousSeriesId) || string.IsNullOrWhiteSpace(nextSeriesId))
        {
            return "missing_series";
        }

        return "different_series";
    }

    private static bool IsEpisode(string? itemType)
    {
        return string.Equals(itemType, "Episode", StringComparison.OrdinalIgnoreCase);
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
