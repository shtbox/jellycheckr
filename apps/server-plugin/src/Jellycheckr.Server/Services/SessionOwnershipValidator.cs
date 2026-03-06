using Jellycheckr.Server.Infrastructure;
using Jellycheckr.Server.Models;
using Microsoft.Extensions.Logging;

namespace Jellycheckr.Server.Services;

public interface ISessionOwnershipValidator
{
    bool CanMutateSession(string? userId, string sessionId);
}

public sealed class SessionOwnershipValidator : ISessionOwnershipValidator
{
    private readonly IServerFallbackSessionSnapshotProvider _sessionSnapshotProvider;
    private readonly ISessionStateStore _sessionStateStore;
    private readonly ILogger<SessionOwnershipValidator> _logger;

    public SessionOwnershipValidator(
        IServerFallbackSessionSnapshotProvider sessionSnapshotProvider,
        ISessionStateStore sessionStateStore,
        ILogger<SessionOwnershipValidator> logger)
    {
        _sessionSnapshotProvider = sessionSnapshotProvider;
        _sessionStateStore = sessionStateStore;
        _logger = logger;
    }

    public bool CanMutateSession(string? userId, string sessionId)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        if (SessionOwnedByCurrentSnapshot(userId, sessionId))
        {
            return true;
        }

        var state = FindState(sessionId);
        if (state is null || string.IsNullOrWhiteSpace(state.UserId))
        {
            _logger.LogJellycheckrTrace(
                "Session ownership unresolved session={SessionId} userId={UserId}",
                JellycheckrLogSanitizer.RedactIdentifier(sessionId),
                JellycheckrLogSanitizer.RedactIdentifier(userId));
            return false;
        }

        var allowed = string.Equals(state.UserId, userId, StringComparison.OrdinalIgnoreCase);
        _logger.LogJellycheckrTrace(
            "Session ownership resolved from state session={SessionId} userId={UserId} allowed={Allowed}",
            JellycheckrLogSanitizer.RedactIdentifier(sessionId),
            JellycheckrLogSanitizer.RedactIdentifier(userId),
            allowed);
        return allowed;
    }

    private bool SessionOwnedByCurrentSnapshot(string userId, string sessionId)
    {
        var snapshots = _sessionSnapshotProvider.GetCurrentSessions();
        var owned = snapshots.Any(snapshot =>
            string.Equals(snapshot.SessionId, sessionId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(snapshot.UserId, userId, StringComparison.OrdinalIgnoreCase));

        if (owned)
        {
            return true;
        }

        return false;
    }

    private SessionState? FindState(string sessionId)
    {
        return _sessionStateStore.Snapshot()
            .FirstOrDefault(state => string.Equals(state.SessionId, sessionId, StringComparison.OrdinalIgnoreCase));
    }
}
