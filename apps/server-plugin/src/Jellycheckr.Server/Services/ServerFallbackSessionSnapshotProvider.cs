using System.Reflection;
using Jellycheckr.Server.Infrastructure;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellycheckr.Server.Services;

public interface IServerFallbackSessionSnapshotProvider
{
    IReadOnlyList<ServerObservedSessionSnapshot> GetCurrentSessions();
}

public sealed class ServerFallbackSessionSnapshotProvider : IServerFallbackSessionSnapshotProvider
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ServerFallbackSessionSnapshotProvider> _logger;

    public ServerFallbackSessionSnapshotProvider(
        IServiceProvider serviceProvider,
        ILogger<ServerFallbackSessionSnapshotProvider> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public IReadOnlyList<ServerObservedSessionSnapshot> GetCurrentSessions()
    {
        var sessionManager = _serviceProvider.GetService<ISessionManager>();
        if (sessionManager is null)
        {
            _logger.LogJellycheckrWarning("[Jellycheckr] ISessionManager is unavailable; server fallback monitoring is disabled.");
            return Array.Empty<ServerObservedSessionSnapshot>();
        }

        var sessions = sessionManager.Sessions?.ToArray() ?? Array.Empty<SessionInfo>();
        if (sessions.Length == 0)
        {
            return Array.Empty<ServerObservedSessionSnapshot>();
        }

        var snapshots = new List<ServerObservedSessionSnapshot>(sessions.Length);
        foreach (var session in sessions)
        {
            try
            {
                var snapshot = CreateSnapshot(session);
                if (!string.IsNullOrWhiteSpace(snapshot.SessionId))
                {
                    snapshots.Add(snapshot);
                }
            }
            catch (Exception ex)
            {
                _logger.LogJellycheckrDebug(ex, "[Jellycheckr] Failed to snapshot a Jellyfin session for server fallback.");
            }
        }

        return snapshots;
    }

    private static ServerObservedSessionSnapshot CreateSnapshot(SessionInfo session)
    {
        var nowPlaying = session.NowPlayingItem;
        var playState = GetPropertyValue<object>(session, "PlayState") ?? GetPropertyValue<object>(session, "PlayerStateInfo");

        var itemId = TryReadId(nowPlaying);
        var itemName = GetPropertyValue<string>(nowPlaying, "Name");
        var mediaType = GetPropertyValue<string>(nowPlaying, "MediaType");
        var itemType = GetPropertyValue<string>(nowPlaying, "Type");

        var isPaused = GetPropertyValue<bool?>(session, "IsPaused")
            ?? GetPropertyValue<bool?>(playState, "IsPaused");

        var positionTicks = GetPropertyValue<long?>(session, "NowPlayingPositionTicks")
            ?? GetPropertyValue<long?>(playState, "PositionTicks");

        return new ServerObservedSessionSnapshot
        {
            SessionId = session.Id,
            UserId = ToStringOrNull(session.UserId),
            UserName = session.UserName,
            ClientName = session.Client,
            DeviceName = session.DeviceName,
            DeviceId = session.DeviceId,
            IsActive = session.IsActive,
            ItemId = itemId,
            ItemName = itemName,
            MediaType = !string.IsNullOrWhiteSpace(mediaType) ? mediaType : itemType,
            PositionTicks = positionTicks,
            IsPaused = isPaused,
            LastActivityUtc = ToDateTimeOffset(session.LastActivityDate),
            LastPlaybackCheckInUtc = ToDateTimeOffset(session.LastPlaybackCheckIn),
            LastPausedUtc = ToDateTimeOffset(session.LastPausedDate)
        };
    }

    private static string? TryReadId(object? value)
    {
        if (value is null)
        {
            return null;
        }

        var directId = GetPropertyValue<string>(value, "Id");
        if (!string.IsNullOrWhiteSpace(directId))
        {
            return directId;
        }

        var idObj = GetRawPropertyValue(value, "Id");
        return ToStringOrNull(idObj);
    }

    private static object? GetRawPropertyValue(object? instance, string propertyName)
    {
        if (instance is null)
        {
            return null;
        }

        var prop = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        return prop?.GetValue(instance);
    }

    private static T? GetPropertyValue<T>(object? instance, string propertyName)
    {
        var raw = GetRawPropertyValue(instance, propertyName);
        if (raw is null)
        {
            return default;
        }

        if (raw is T typed)
        {
            return typed;
        }

        try
        {
            var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            var converted = Convert.ChangeType(raw, targetType);
            return converted is null ? default : (T?)converted;
        }
        catch
        {
            return default;
        }
    }

    private static DateTimeOffset? ToDateTimeOffset(object? value)
    {
        return value switch
        {
            null => null,
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, dt.Kind == DateTimeKind.Unspecified ? DateTimeKind.Utc : dt.Kind)),
            _ => null
        };
    }

    private static string? ToStringOrNull(object? value)
    {
        var text = value?.ToString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}

public sealed class ServerObservedSessionSnapshot
{
    public string SessionId { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? UserName { get; set; }
    public string? ClientName { get; set; }
    public string? DeviceName { get; set; }
    public string? DeviceId { get; set; }
    public bool IsActive { get; set; }
    public string? ItemId { get; set; }
    public string? ItemName { get; set; }
    public string? MediaType { get; set; }
    public long? PositionTicks { get; set; }
    public bool? IsPaused { get; set; }
    public DateTimeOffset? LastActivityUtc { get; set; }
    public DateTimeOffset? LastPlaybackCheckInUtc { get; set; }
    public DateTimeOffset? LastPausedUtc { get; set; }
}

