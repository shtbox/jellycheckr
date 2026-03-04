using Microsoft.Extensions.Logging;

namespace Jellycheckr.Server.Services;

public interface IWebClientSessionResolver
{
    ServerObservedSessionSnapshot? Resolve(string? userId, string deviceId);
}

public sealed class WebClientSessionResolver : IWebClientSessionResolver
{
    private readonly IServerFallbackSessionSnapshotProvider _sessionSnapshotProvider;
    private readonly ILogger<WebClientSessionResolver> _logger;

    public WebClientSessionResolver(
        IServerFallbackSessionSnapshotProvider sessionSnapshotProvider,
        ILogger<WebClientSessionResolver> logger)
    {
        _sessionSnapshotProvider = sessionSnapshotProvider;
        _logger = logger;
    }

    public ServerObservedSessionSnapshot? Resolve(string? userId, string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            _logger.LogJellycheckrTrace("Web client session resolution skipped because deviceId was missing.");
            return null;
        }

        var candidates = _sessionSnapshotProvider.GetCurrentSessions()
            .Where(snapshot => string.Equals(snapshot.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(snapshot => UserMatches(snapshot, userId))
            .ThenByDescending(HasCurrentItem)
            .ThenByDescending(IsLikelyWebClient)
            .ThenByDescending(snapshot => snapshot.IsActive)
            .ThenByDescending(snapshot => snapshot.LastActivityUtc ?? DateTimeOffset.MinValue)
            .ToArray();

        var resolved = candidates.FirstOrDefault();
        _logger.LogJellycheckrTrace(
            "Web client session resolution deviceId={DeviceId} userId={UserId} candidates={CandidateCount} resolvedSessionId={SessionId}",
            deviceId,
            userId ?? "(null)",
            candidates.Length,
            resolved?.SessionId ?? "(none)");

        return resolved;
    }

    private static bool UserMatches(ServerObservedSessionSnapshot snapshot, string? userId)
    {
        return !string.IsNullOrWhiteSpace(userId)
               && string.Equals(snapshot.UserId, userId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasCurrentItem(ServerObservedSessionSnapshot snapshot)
    {
        return !string.IsNullOrWhiteSpace(snapshot.ItemId);
    }

    private static bool IsLikelyWebClient(ServerObservedSessionSnapshot snapshot)
    {
        var client = snapshot.ClientName ?? string.Empty;
        var device = snapshot.DeviceName ?? string.Empty;
        return client.Contains("web", StringComparison.OrdinalIgnoreCase)
               || device.Contains("browser", StringComparison.OrdinalIgnoreCase);
    }
}
