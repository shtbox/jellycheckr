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
}
