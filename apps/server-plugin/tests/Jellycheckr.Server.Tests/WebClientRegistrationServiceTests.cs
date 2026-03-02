using Jellycheckr.Contracts;
using Jellycheckr.Server.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellycheckr.Server.Tests;

public sealed class WebClientRegistrationServiceTests
{
    [Fact]
    public void Register_UsesResolvedSessionAndMarksLease()
    {
        var now = DateTimeOffset.Parse("2026-03-02T09:00:00Z");
        var clock = new FakeClock(now);
        var store = new SessionStateStore(NullLogger<SessionStateStore>.Instance);
        var snapshots = new StubSessionSnapshotProvider(
            new[]
            {
                CreateSnapshot("s1", "u1", "dev-1")
            });
        var service = CreateService(store, snapshots, clock);

        var response = service.Register("u1", new WebClientRegisterRequest { DeviceId = "dev-1" });

        var state = store.GetOrCreate("s1");
        Assert.True(response.Registered);
        Assert.Equal("s1", response.SessionId);
        Assert.NotNull(response.Config);
        Assert.True(state.WebUiRegistered);
        Assert.Equal(now.Add(WebUiRegistrationLeasePolicy.LeaseDuration), state.WebUiRegistrationLeaseUtc);
        Assert.Equal("item-1", state.CurrentItemId);
    }

    [Fact]
    public void Register_ReturnsUnresolved_WhenNoMatchingSessionExists()
    {
        var store = new SessionStateStore(NullLogger<SessionStateStore>.Instance);
        var snapshots = new StubSessionSnapshotProvider();
        var service = CreateService(store, snapshots, new FakeClock(DateTimeOffset.Parse("2026-03-02T09:00:00Z")));

        var response = service.Register("u1", new WebClientRegisterRequest { DeviceId = "missing" });

        Assert.False(response.Registered);
        Assert.Equal("session_unresolved", response.Reason);
        Assert.Empty(store.Snapshot());
    }

    [Fact]
    public void Heartbeat_RenewsLease_AndUnregister_ClearsRegistration()
    {
        var start = DateTimeOffset.Parse("2026-03-02T09:10:00Z");
        var clock = new FakeClock(start);
        var store = new SessionStateStore(NullLogger<SessionStateStore>.Instance);
        var snapshots = new StubSessionSnapshotProvider(
            new[]
            {
                CreateSnapshot("s1", "u1", "dev-1")
            });
        var service = CreateService(store, snapshots, clock);

        _ = service.Register("u1", new WebClientRegisterRequest { DeviceId = "dev-1" });
        clock.UtcNow = start.AddSeconds(30);

        var heartbeat = service.Heartbeat("u1", new WebClientHeartbeatRequest
        {
            DeviceId = "dev-1",
            SessionId = "s1"
        });

        var state = store.GetOrCreate("s1");
        Assert.True(heartbeat.Accepted);
        Assert.Equal(start.AddSeconds(30).Add(WebUiRegistrationLeasePolicy.LeaseDuration), state.WebUiRegistrationLeaseUtc);

        service.Unregister("u1", new WebClientUnregisterRequest { SessionId = "s1" });

        Assert.False(state.WebUiRegistered);
        Assert.Null(state.WebUiRegistrationLeaseUtc);
    }

    private static WebClientRegistrationService CreateService(
        SessionStateStore store,
        StubSessionSnapshotProvider snapshots,
        FakeClock clock)
    {
        return new WebClientRegistrationService(
            store,
            new StubConfigService(),
            new WebClientSessionResolver(snapshots, NullLogger<WebClientSessionResolver>.Instance),
            new SessionStateObserver(NullLogger<SessionStateObserver>.Instance),
            clock,
            NullLogger<WebClientRegistrationService>.Instance);
    }

    private static ServerObservedSessionSnapshot CreateSnapshot(string sessionId, string userId, string deviceId)
    {
        return new ServerObservedSessionSnapshot
        {
            SessionId = sessionId,
            UserId = userId,
            DeviceId = deviceId,
            ClientName = "Jellyfin Web",
            DeviceName = "Browser",
            ItemId = "item-1",
            ItemName = "Episode 1",
            IsActive = true,
            LastActivityUtc = DateTimeOffset.Parse("2026-03-02T09:00:00Z")
        };
    }
}
