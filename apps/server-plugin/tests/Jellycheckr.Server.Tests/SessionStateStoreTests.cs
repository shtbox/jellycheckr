using Jellycheckr.Server.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellycheckr.Server.Tests;

public sealed class SessionStateStoreTests
{
    [Fact]
    public void GetOrCreate_ReturnsSameInstance_PerSession()
    {
        var store = new SessionStateStore(NullLogger<SessionStateStore>.Instance);
        var first = store.GetOrCreate("s1");
        var second = store.GetOrCreate("s1");
        Assert.Same(first, second);
    }

    [Fact]
    public void PruneOlderThan_RemovesNeverSeenState_UsingCreatedUtc()
    {
        var store = new SessionStateStore(NullLogger<SessionStateStore>.Instance);
        var state = store.GetOrCreate("s1");
        state.CreatedUtc = DateTimeOffset.Parse("2026-02-20T10:00:00Z");
        state.LastSeenUtc = DateTimeOffset.MinValue;

        var removed = store.PruneOlderThan(DateTimeOffset.Parse("2026-02-21T10:00:00Z"));

        Assert.Equal(1, removed);
        Assert.Empty(store.Snapshot());
    }

    [Fact]
    public void PruneOlderThan_KeepsRecentNeverSeenState_UsingCreatedUtc()
    {
        var store = new SessionStateStore(NullLogger<SessionStateStore>.Instance);
        var state = store.GetOrCreate("s1");
        state.CreatedUtc = DateTimeOffset.Parse("2026-02-21T10:00:00Z");
        state.LastSeenUtc = DateTimeOffset.MinValue;

        var removed = store.PruneOlderThan(DateTimeOffset.Parse("2026-02-21T09:59:00Z"));

        Assert.Equal(0, removed);
        Assert.Single(store.Snapshot());
    }
}
