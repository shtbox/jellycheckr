using System.Reflection;
using Jellycheckr.Server.Services;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Dto;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellycheckr.Server.Tests;

public sealed class ServerFallbackSessionSnapshotProviderTests
{
    [Fact]
    public void CreateSnapshot_PreservesItemTypeAndSeriesIdFromJellyfinSession()
    {
        var itemId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var seriesId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var session = new SessionInfo(null!, NullLogger.Instance)
        {
            Id = "session-1",
            UserName = "user-1",
            NowPlayingItem = new BaseItemDto
            {
                Id = itemId,
                Name = "Episode 1",
                MediaType = MediaType.Video,
                Type = BaseItemKind.Episode,
                SeriesId = seriesId
            }
        };

        var snapshot = CreateSnapshot(session);

        Assert.Equal(itemId.ToString(), snapshot.ItemId);
        Assert.Equal("Video", snapshot.MediaType);
        Assert.Equal("Episode", snapshot.ItemType);
        Assert.Equal(seriesId.ToString(), snapshot.SeriesId);
    }

    private static ServerObservedSessionSnapshot CreateSnapshot(SessionInfo session)
    {
        var method = typeof(ServerFallbackSessionSnapshotProvider)
            .GetMethod("CreateSnapshot", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        return Assert.IsType<ServerObservedSessionSnapshot>(
            method.Invoke(null, new object[] { session }));
    }
}
