using System.Reflection;
using Jellycheckr.Server.Models;
using Jellycheckr.Server.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellycheckr.Server.Tests;

public sealed class ServerFallbackEnforcementServiceInferenceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-02-26T18:00:00Z");

    [Fact]
    public void PassiveHeartbeatObservation_DoesNotAdvanceInferredActivity()
    {
        var state = new SessionState
        {
            SessionId = "s1",
            CurrentItemId = "item1",
            LastObservedPositionTicks = TimeSpan.FromMinutes(10).Ticks,
            LastPlaybackProgressObservedUtc = Now.AddSeconds(-10),
            LastObservedLastActivityDateUtc = Now.AddMinutes(-2),
            LastObservedLastPlaybackCheckInUtc = Now.AddMinutes(-2),
            LastInferredActivityUtc = Now.AddMinutes(-30),
            IsPaused = false
        };
        var snapshot = new ServerObservedSessionSnapshot
        {
            SessionId = "s1",
            ItemId = "item1",
            PositionTicks = TimeSpan.FromMinutes(10).Ticks + TimeSpan.FromSeconds(10).Ticks,
            IsPaused = false,
            LastActivityUtc = Now,
            LastPlaybackCheckInUtc = Now.AddSeconds(-20)
        };

        InvokeObserveSession(state, snapshot, Now);

        Assert.Equal(Now.AddMinutes(-30), state.LastInferredActivityUtc);
    }

    [Fact]
    public void EpisodeTransition_AfterShortNoItemGap_IncrementsEpisodeCounter()
    {
        var state = new SessionState { SessionId = "s1" };

        InvokeObserveSession(state, CreateSnapshot("s1", "item-a", "Episode", "series-1"), Now.AddSeconds(-15));
        InvokeObserveSession(state, CreateSnapshot("s1", null), Now.AddSeconds(-10));
        InvokeObserveSession(state, CreateSnapshot("s1", null), Now.AddSeconds(-5));
        InvokeObserveSession(state, CreateSnapshot("s1", "item-b", "Episode", "series-1"), Now);

        Assert.Equal(1, state.ServerFallbackEpisodeTransitionsSinceReset);
        Assert.Equal("item-a", state.PreviousItemId);
        Assert.Equal("item-b", state.CurrentItemId);
        Assert.Null(state.NoCurrentItemSinceUtc);
    }

    [Fact]
    public void SameItem_AfterNoItemGap_DoesNotIncrementEpisodeCounter()
    {
        var state = new SessionState { SessionId = "s1" };

        InvokeObserveSession(state, CreateSnapshot("s1", "item-a", "Episode", "series-1"), Now.AddSeconds(-15));
        InvokeObserveSession(state, CreateSnapshot("s1", null), Now.AddSeconds(-10));
        InvokeObserveSession(state, CreateSnapshot("s1", "item-a", "Episode", "series-1"), Now);

        Assert.Equal(0, state.ServerFallbackEpisodeTransitionsSinceReset);
        Assert.Equal("item-a", state.PreviousItemId);
        Assert.Equal("item-a", state.CurrentItemId);
    }

    [Fact]
    public void MovieTransition_DoesNotIncrementEpisodeCounter()
    {
        var state = new SessionState { SessionId = "s1" };

        InvokeObserveSession(state, CreateSnapshot("s1", "movie-a", "Movie"), Now.AddMinutes(-2));
        InvokeObserveSession(state, CreateSnapshot("s1", "movie-b", "Movie"), Now);

        Assert.Equal(0, state.ServerFallbackEpisodeTransitionsSinceReset);
    }

    [Fact]
    public void MixedMediaTransition_DoesNotIncrementEpisodeCounter()
    {
        var state = new SessionState { SessionId = "s1" };

        InvokeObserveSession(state, CreateSnapshot("s1", "episode-a", "Episode", "series-1"), Now.AddMinutes(-3));
        InvokeObserveSession(state, CreateSnapshot("s1", "movie-a", "Movie"), Now.AddMinutes(-2));
        InvokeObserveSession(state, CreateSnapshot("s1", "episode-b", "Episode", "series-1"), Now);

        Assert.Equal(0, state.ServerFallbackEpisodeTransitionsSinceReset);
    }

    [Fact]
    public void EpisodeTransition_DifferentSeries_DoesNotIncrementEpisodeCounter()
    {
        var state = new SessionState { SessionId = "s1" };

        InvokeObserveSession(state, CreateSnapshot("s1", "episode-a", "Episode", "series-1"), Now.AddMinutes(-2));
        InvokeObserveSession(state, CreateSnapshot("s1", "episode-b", "Episode", "series-2"), Now);

        Assert.Equal(0, state.ServerFallbackEpisodeTransitionsSinceReset);
    }

    [Fact]
    public void SeekJumpObservation_AdvancesInferredActivity()
    {
        var state = new SessionState
        {
            SessionId = "s1",
            CurrentItemId = "item1",
            LastObservedPositionTicks = TimeSpan.FromMinutes(10).Ticks,
            LastPlaybackProgressObservedUtc = Now.AddSeconds(-10),
            LastInferredActivityUtc = Now.AddMinutes(-30),
            IsPaused = false
        };
        var snapshot = new ServerObservedSessionSnapshot
        {
            SessionId = "s1",
            ItemId = "item1",
            PositionTicks = TimeSpan.FromMinutes(13).Ticks,
            IsPaused = false
        };

        InvokeObserveSession(state, snapshot, Now);

        Assert.Equal(Now, state.LastInferredActivityUtc);
    }

    [Fact]
    public void ResumeObservation_AdvancesInferredActivity()
    {
        var state = new SessionState
        {
            SessionId = "s1",
            CurrentItemId = "item1",
            LastInferredActivityUtc = Now.AddMinutes(-30),
            IsPaused = true
        };
        var snapshot = new ServerObservedSessionSnapshot
        {
            SessionId = "s1",
            ItemId = "item1",
            IsPaused = false
        };

        InvokeObserveSession(state, snapshot, Now);

        Assert.Equal(Now, state.LastInferredActivityUtc);
    }

    [Fact]
    public void ApplyFallbackResetAndCooldown_WithCooldownEnabled_SetsNextEligiblePrompt()
    {
        var state = new SessionState
        {
            NextEligiblePromptUtc = DateTimeOffset.MinValue,
            ServerFallbackEpisodeTransitionsSinceReset = 3,
            ServerFallbackPlaybackTicksSinceReset = TimeSpan.FromMinutes(90).Ticks
        };

        InvokeApplyFallbackResetAndCooldown(state, Now, cooldownMinutes: 30, applyCooldown: true);

        Assert.Equal(Now.AddMinutes(30), state.NextEligiblePromptUtc);
        Assert.Equal(0, state.ServerFallbackEpisodeTransitionsSinceReset);
        Assert.Equal(0, state.ServerFallbackPlaybackTicksSinceReset);
    }

    [Fact]
    public void ApplyFallbackResetAndCooldown_WithCooldownDisabled_ClearsNextEligiblePromptGate()
    {
        var state = new SessionState
        {
            NextEligiblePromptUtc = Now.AddMinutes(30),
            ServerFallbackEpisodeTransitionsSinceReset = 2,
            ServerFallbackPlaybackTicksSinceReset = TimeSpan.FromMinutes(15).Ticks
        };

        InvokeApplyFallbackResetAndCooldown(state, Now, cooldownMinutes: 30, applyCooldown: false);

        Assert.Equal(DateTimeOffset.MinValue, state.NextEligiblePromptUtc);
        Assert.Equal(0, state.ServerFallbackEpisodeTransitionsSinceReset);
        Assert.Equal(0, state.ServerFallbackPlaybackTicksSinceReset);
    }

    [Fact]
    public void SustainedNoItemGap_ResetsFallbackWindow()
    {
        var state = new SessionState
        {
            SessionId = "s1",
            CurrentItemId = "episode-a",
            CurrentItemType = "Episode",
            CurrentSeriesId = "series-1",
            PromptActive = true,
            PromptDeadlineUtc = Now.AddMinutes(1),
            ServerFallbackEpisodeTransitionsSinceReset = 2,
            ServerFallbackPlaybackTicksSinceReset = TimeSpan.FromMinutes(45).Ticks
        };

        InvokeObserveSession(state, CreateSnapshot("s1", null), Now.AddSeconds(-25));
        InvokeObserveSession(state, CreateSnapshot("s1", null), Now);

        Assert.Equal(0, state.ServerFallbackEpisodeTransitionsSinceReset);
        Assert.Equal(0, state.ServerFallbackPlaybackTicksSinceReset);
        Assert.False(state.PromptActive);
        Assert.Null(state.PromptDeadlineUtc);
        Assert.Null(state.PreviousItemId);
        Assert.Equal(Now.AddSeconds(-25), state.NoCurrentItemSinceUtc);
        Assert.Equal("playback_reset", state.LastFallbackAction);
        Assert.Equal("no_current_item_gap", state.LastFallbackActionResult);
    }

    [Fact]
    public void PlaybackResumedAfterSustainedNoItemGap_StartsFreshSessionWindow()
    {
        var state = new SessionState
        {
            SessionId = "s1",
            CurrentItemId = "episode-a",
            CurrentItemType = "Episode",
            CurrentSeriesId = "series-1",
            LastInferredActivityUtc = Now.AddHours(-3),
            ServerFallbackEpisodeTransitionsSinceReset = 2,
            ServerFallbackPlaybackTicksSinceReset = TimeSpan.FromMinutes(90).Ticks
        };

        InvokeObserveSession(state, CreateSnapshot("s1", null), Now.AddSeconds(-25));
        InvokeObserveSession(state, CreateSnapshot("s1", null), Now.AddSeconds(-1));
        InvokeObserveSession(state, CreateSnapshot("s1", "episode-b", "Episode", "series-1"), Now);

        Assert.Equal("episode-b", state.CurrentItemId);
        Assert.Equal("Episode", state.CurrentItemType);
        Assert.Equal("series-1", state.CurrentSeriesId);
        Assert.Equal(0, state.ServerFallbackEpisodeTransitionsSinceReset);
        Assert.Equal(0, state.ServerFallbackPlaybackTicksSinceReset);
        Assert.Equal(Now, state.LastInferredActivityUtc);
        Assert.Null(state.PreviousItemId);
        Assert.Null(state.NoCurrentItemSinceUtc);
        Assert.Equal("playback_reset", state.LastFallbackAction);
        Assert.Equal("playback_resumed_after_gap", state.LastFallbackActionResult);
    }

    private static void InvokeObserveSession(
        SessionState state,
        ServerObservedSessionSnapshot snapshot,
        DateTimeOffset nowUtc)
    {
        var method = typeof(ServerFallbackEnforcementService).GetMethod(
            "ObserveSession",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        method!.Invoke(CreateService(nowUtc), new object[] { state, snapshot, nowUtc });
    }

    private static void InvokeApplyFallbackResetAndCooldown(
        SessionState state,
        DateTimeOffset nowUtc,
        int cooldownMinutes,
        bool applyCooldown)
    {
        var method = typeof(ServerFallbackEnforcementService).GetMethod(
            "ApplyFallbackResetAndCooldown",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        method!.Invoke(null, new object[] { state, nowUtc, cooldownMinutes, applyCooldown });
    }

    private static ServerObservedSessionSnapshot CreateSnapshot(
        string sessionId,
        string? itemId,
        string? itemType = null,
        string? seriesId = null)
    {
        return new ServerObservedSessionSnapshot
        {
            SessionId = sessionId,
            ItemId = itemId,
            ItemType = itemType,
            SeriesId = seriesId
        };
    }

    private static ServerFallbackEnforcementService CreateService(DateTimeOffset nowUtc)
    {
        return new ServerFallbackEnforcementService(
            new SessionStateStore(NullLogger<SessionStateStore>.Instance),
            new StubConfigService(),
            new StubSessionSnapshotProvider(),
            new SessionStateObserver(NullLogger<SessionStateObserver>.Instance),
            new ServerFallbackDecisionEngine(),
            new StubJellyfinSessionCommandDispatcher(),
            new FakeClock(nowUtc),
            NullLogger<ServerFallbackEnforcementService>.Instance);
    }
}
