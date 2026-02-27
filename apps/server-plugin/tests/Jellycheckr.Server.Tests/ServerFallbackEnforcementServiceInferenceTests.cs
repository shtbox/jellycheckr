using System.Reflection;
using Jellycheckr.Server.Models;
using Jellycheckr.Server.Services;

namespace Jellycheckr.Server.Tests;

public sealed class ServerFallbackEnforcementServiceInferenceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-02-26T18:00:00Z");

    [Fact]
    public void PassiveLastActivityUpdate_WithoutPlaybackCheckInAdvance_DoesNotCountAsMeaningful()
    {
        var state = new SessionState
        {
            LastObservedLastActivityDateUtc = Now.AddMinutes(-2),
            LastObservedLastPlaybackCheckInUtc = Now.AddMinutes(-2)
        };
        var snapshot = new ServerObservedSessionSnapshot
        {
            LastActivityUtc = Now,
            LastPlaybackCheckInUtc = Now.AddMinutes(-2)
        };

        var meaningful = InvokeHasMeaningfulLastActivityChange(state, snapshot);

        Assert.False(meaningful);
    }

    [Fact]
    public void LastActivityUpdate_WithPlaybackCheckInAdvanceAndDelta_IsMeaningful()
    {
        var state = new SessionState
        {
            LastObservedLastActivityDateUtc = Now.AddMinutes(-2),
            LastObservedLastPlaybackCheckInUtc = Now.AddMinutes(-2)
        };
        var snapshot = new ServerObservedSessionSnapshot
        {
            LastActivityUtc = Now,
            LastPlaybackCheckInUtc = Now.AddSeconds(-20)
        };

        var meaningful = InvokeHasMeaningfulLastActivityChange(state, snapshot);

        Assert.True(meaningful);
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

    private static bool InvokeHasMeaningfulLastActivityChange(
        SessionState state,
        ServerObservedSessionSnapshot snapshot)
    {
        var method = typeof(ServerFallbackEnforcementService).GetMethod(
            "HasMeaningfulLastActivityChange",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var result = method!.Invoke(null, new object[] { state, snapshot });
        Assert.IsType<bool>(result);
        return (bool)result!;
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
}
