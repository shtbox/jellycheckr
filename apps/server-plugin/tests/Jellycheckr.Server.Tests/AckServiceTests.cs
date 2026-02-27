using Jellycheckr.Contracts;
using Jellycheckr.Server.Infrastructure;
using Jellycheckr.Server.Models;
using Jellycheckr.Server.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellycheckr.Server.Tests;

public sealed class AckServiceTests
{
    [Fact]
    public void ContinueAck_ResetsCountersAndSetsCooldown()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-02-22T20:00:00Z"));
        var configService = new StubConfigService();
        var store = new SessionStateStore(NullLogger<SessionStateStore>.Instance);
        var svc = new AckService(store, clock, NullLogger<AckService>.Instance);
        var config = new EffectiveConfigResponse { CooldownMinutes = 30 };

        var response = svc.HandleAck("s1", new AckRequest { AckType = "continue", Reason = "test" }, config);

        Assert.True(response.ResetApplied);
        Assert.Equal(DateTimeOffset.Parse("2026-02-22T20:30:00Z"), response.NextEligiblePromptUtc);
    }

    [Fact]
    public void Interaction_UpdatesLastInteraction()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-02-22T20:10:00Z"));
        var store = new SessionStateStore(NullLogger<SessionStateStore>.Instance);
        var svc = new AckService(store, clock, NullLogger<AckService>.Instance);

        var response = svc.HandleInteraction("s1", new InteractionRequest { EventType = "keydown", ItemId = "item1" });
        var state = store.GetOrCreate("s1");

        Assert.True(response.Accepted);
        Assert.Equal(DateTimeOffset.Parse("2026-02-22T20:10:00Z"), state.LastInteractionUtc);
        Assert.Equal("item1", state.LastItemId);
    }

    [Fact]
    public void ContinueAck_AlsoResetsServerFallbackState()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-02-22T20:20:00Z"));
        var store = new SessionStateStore(NullLogger<SessionStateStore>.Instance);
        var svc = new AckService(store, clock, NullLogger<AckService>.Instance);
        var state = store.GetOrCreate("s1");
        state.ServerFallbackEpisodeTransitionsSinceReset = 4;
        state.ServerFallbackPlaybackTicksSinceReset = TimeSpan.FromMinutes(90).Ticks;
        state.FallbackPhase = ServerFallbackPhase.PauseGracePending;
        state.PauseIssuedUtc = clock.UtcNow.AddSeconds(-10);
        state.PauseGraceDeadlineUtc = clock.UtcNow.AddSeconds(20);

        _ = svc.HandleAck("s1", new AckRequest { AckType = "continue", Reason = "test" }, new EffectiveConfigResponse { CooldownMinutes = 5 });

        Assert.Equal(0, state.ServerFallbackEpisodeTransitionsSinceReset);
        Assert.Equal(0, state.ServerFallbackPlaybackTicksSinceReset);
        Assert.Equal(ServerFallbackPhase.Monitoring, state.FallbackPhase);
        Assert.Null(state.PauseIssuedUtc);
        Assert.Null(state.PauseGraceDeadlineUtc);
    }
}
