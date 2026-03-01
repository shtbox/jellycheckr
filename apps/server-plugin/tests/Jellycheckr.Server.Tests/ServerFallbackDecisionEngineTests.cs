using Jellycheckr.Contracts;
using Jellycheckr.Server.Models;
using Jellycheckr.Server.Services;

namespace Jellycheckr.Server.Tests;

public sealed class ServerFallbackDecisionEngineTests
{
    private readonly ServerFallbackDecisionEngine _engine = new();
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-02-24T12:00:00Z");

    [Fact]
    public void DoesNotTrigger_WhenFallbackDisabled()
    {
        var state = BaseState();
        var config = BaseConfig();
        config.EnableServerFallback = false;

        var decision = _engine.Evaluate(state, config, Now);

        Assert.False(decision.ShouldTrigger);
        Assert.Equal("fallback_disabled", decision.Reason);
    }

    [Fact]
    public void Triggers_WhenEpisodeThresholdMet_AndInactive()
    {
        var state = BaseState();
        state.ServerFallbackEpisodeTransitionsSinceReset = 3;
        state.ServerFallbackPlaybackTicksSinceReset = TimeSpan.FromMinutes(10).Ticks;
        state.LastInferredActivityUtc = Now.AddMinutes(-35);

        var config = BaseConfig();
        config.EnableEpisodeCheck = true;
        config.EnableTimerCheck = false;
        config.EpisodeThreshold = 3;
        config.ServerFallbackInactivityMinutes = 30;

        var decision = _engine.Evaluate(state, config, Now);

        Assert.True(decision.ShouldTrigger);
    }

    [Fact]
    public void Triggers_WhenMinutesThresholdMet_AndInactive()
    {
        var state = BaseState();
        state.ServerFallbackPlaybackTicksSinceReset = TimeSpan.FromMinutes(121).Ticks;
        state.LastInferredActivityUtc = Now.AddMinutes(-35);

        var config = BaseConfig();
        config.EnableEpisodeCheck = false;
        config.EnableTimerCheck = true;
        config.MinutesThreshold = 120;
        config.ServerFallbackInactivityMinutes = 30;

        var decision = _engine.Evaluate(state, config, Now);

        Assert.True(decision.ShouldTrigger);
    }

    [Fact]
    public void DoesNotTrigger_WhenThresholdMetButInactivityNotMet()
    {
        var state = BaseState();
        state.ServerFallbackEpisodeTransitionsSinceReset = 5;
        state.LastInferredActivityUtc = Now.AddMinutes(-5);

        var config = BaseConfig();
        config.ServerFallbackInactivityMinutes = 30;

        var decision = _engine.Evaluate(state, config, Now);

        Assert.False(decision.ShouldTrigger);
        Assert.Equal("inactivity_not_met", decision.Reason);
    }

    [Fact]
    public void DoesNotTrigger_WhenMinutesThresholdMetButRecentInteractionExists()
    {
        var state = BaseState();
        state.ServerFallbackPlaybackTicksSinceReset = TimeSpan.FromMinutes(121).Ticks;
        state.LastInferredActivityUtc = Now.AddHours(-2);
        state.LastInteractionUtc = Now.AddMinutes(-5);

        var config = BaseConfig();
        config.EnableEpisodeCheck = false;
        config.EnableTimerCheck = true;
        config.MinutesThreshold = 120;
        config.ServerFallbackInactivityMinutes = 30;

        var decision = _engine.Evaluate(state, config, Now);

        Assert.False(decision.ShouldTrigger);
        Assert.Equal("inactivity_not_met", decision.Reason);
    }

    [Fact]
    public void Triggers_WhenEitherThresholdMet_WithOrSemantics()
    {
        var state = BaseState();
        state.ServerFallbackEpisodeTransitionsSinceReset = 0;
        state.ServerFallbackPlaybackTicksSinceReset = TimeSpan.FromMinutes(130).Ticks;
        state.LastInferredActivityUtc = Now.AddMinutes(-45);

        var config = BaseConfig();
        config.EnableEpisodeCheck = true;
        config.EnableTimerCheck = true;
        config.EpisodeThreshold = 3;
        config.MinutesThreshold = 120;
        config.ServerFallbackInactivityMinutes = 30;

        var decision = _engine.Evaluate(state, config, Now);

        Assert.True(decision.ShouldTrigger);
    }

    [Fact]
    public void DoesNotTrigger_WhenAllChecksDisabled()
    {
        var state = BaseState();
        state.ServerFallbackPlaybackTicksSinceReset = TimeSpan.FromMinutes(999).Ticks;
        state.ServerFallbackEpisodeTransitionsSinceReset = 999;
        state.LastInferredActivityUtc = Now.AddHours(-2);

        var config = BaseConfig();
        config.EnableEpisodeCheck = false;
        config.EnableTimerCheck = false;

        var decision = _engine.Evaluate(state, config, Now);

        Assert.False(decision.ShouldTrigger);
        Assert.Equal("thresholds_disabled", decision.Reason);
    }

    [Fact]
    public void DoesNotTrigger_WhenInPauseGracePending()
    {
        var state = BaseState();
        state.FallbackPhase = ServerFallbackPhase.PauseGracePending;
        state.ServerFallbackEpisodeTransitionsSinceReset = 99;
        state.ServerFallbackPlaybackTicksSinceReset = TimeSpan.FromMinutes(999).Ticks;
        state.LastInferredActivityUtc = Now.AddHours(-2);

        var decision = _engine.Evaluate(state, BaseConfig(), Now);

        Assert.False(decision.ShouldTrigger);
        Assert.Equal("pause_grace_pending", decision.Reason);
    }

    [Fact]
    public void DeveloperMode_TriggersFastPath_ForServerFallbackTesting()
    {
        var state = BaseState();
        state.ServerFallbackPlaybackTicksSinceReset = TimeSpan.FromSeconds(16).Ticks;
        state.LastInferredActivityUtc = Now;

        var config = BaseConfig();
        config.DeveloperMode = true;
        config.DeveloperPromptAfterSeconds = 15;

        var decision = _engine.Evaluate(state, config, Now);

        Assert.True(decision.ShouldTrigger);
        Assert.Equal("developer_mode_after_15s", decision.Reason);
    }

    [Fact]
    public void DeveloperMode_DoesNotTrigger_WhenPaused()
    {
        var state = BaseState();
        state.ServerFallbackPlaybackTicksSinceReset = TimeSpan.FromSeconds(30).Ticks;
        state.IsPaused = true;

        var config = BaseConfig();
        config.DeveloperMode = true;
        config.DeveloperPromptAfterSeconds = 15;

        var decision = _engine.Evaluate(state, config, Now);

        Assert.False(decision.ShouldTrigger);
        Assert.Equal("paused", decision.Reason);
    }

    private static SessionState BaseState()
    {
        return new SessionState
        {
            SessionId = "s1",
            CurrentItemId = "item1",
            LastSeenUtc = Now,
            LastInferredActivityUtc = Now.AddHours(-1),
            FallbackPhase = ServerFallbackPhase.Monitoring
        };
    }

    private static EffectiveConfigResponse BaseConfig()
    {
        return new EffectiveConfigResponse
        {
            Enabled = true,
            EnableEpisodeCheck = true,
            EnableTimerCheck = true,
            EnableServerFallback = true,
            EpisodeThreshold = 3,
            MinutesThreshold = 120,
            CooldownMinutes = 30,
            ServerFallbackInactivityMinutes = 30,
            ServerFallbackPauseBeforeStop = true,
            ServerFallbackPauseGraceSeconds = 45,
            ServerFallbackSendMessageBeforePause = true,
            ServerFallbackClientMessage = "msg"
        };
    }
}
