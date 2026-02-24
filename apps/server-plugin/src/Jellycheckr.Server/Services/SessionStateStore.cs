using System.Collections.Concurrent;
using Jellycheckr.Server.Infrastructure;
using Jellycheckr.Server.Models;
using Microsoft.Extensions.Logging;

namespace Jellycheckr.Server.Services;

public interface ISessionStateStore
{
    SessionState GetOrCreate(string sessionId);
    IReadOnlyCollection<SessionState> Snapshot();
    bool Remove(string sessionId);
    int PruneOlderThan(DateTimeOffset cutoffUtc);
}

public sealed class SessionStateStore : ISessionStateStore
{
    private readonly ConcurrentDictionary<string, SessionState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly IConfigService _configService;
    private readonly ILogger<SessionStateStore> _logger;

    public SessionStateStore(IConfigService configService, ILogger<SessionStateStore> logger)
    {
        _configService = configService;
        _logger = logger;
    }

    public SessionState GetOrCreate(string sessionId)
    {
        var created = false;
        var state = _states.GetOrAdd(sessionId, id =>
        {
            created = true;
            return new SessionState { SessionId = id };
        });

        if (created)
        {
            _logger.LogInformation("[Jellycheckr] Created session state for session={SessionId}.", sessionId);
        }

        JellycheckrDiagnosticLogging.Verbose(
            _logger,
            _configService,
            "SessionStateStore.GetOrCreate session={SessionId} created={Created} state={@State}",
            sessionId,
            created,
            JellycheckrDiagnosticLogging.Describe(state));

        return state;
    }

    public IReadOnlyCollection<SessionState> Snapshot()
    {
        var snapshot = _states.Values.ToArray();
        JellycheckrDiagnosticLogging.Verbose(
            _logger,
            _configService,
            "SessionStateStore.Snapshot count={Count} sessions={@Sessions}",
            snapshot.Length,
            snapshot.Select(JellycheckrDiagnosticLogging.Describe).ToArray());
        return snapshot;
    }

    public bool Remove(string sessionId)
    {
        var removed = _states.TryRemove(sessionId, out _);
        JellycheckrDiagnosticLogging.Verbose(
            _logger,
            _configService,
            "SessionStateStore.Remove session={SessionId} removed={Removed}",
            sessionId,
            removed);
        return removed;
    }

    public int PruneOlderThan(DateTimeOffset cutoffUtc)
    {
        var removed = 0;
        foreach (var pair in _states)
        {
            if (pair.Value.LastSeenUtc == DateTimeOffset.MinValue || pair.Value.LastSeenUtc >= cutoffUtc)
            {
                continue;
            }

            if (_states.TryRemove(pair.Key, out _))
            {
                removed++;
            }
        }

        if (removed > 0)
        {
            _logger.LogInformation("[Jellycheckr] Pruned {Count} stale session state entries older than {CutoffUtc}.", removed, cutoffUtc);
        }

        JellycheckrDiagnosticLogging.Verbose(
            _logger,
            _configService,
            "SessionStateStore.PruneOlderThan cutoffUtc={CutoffUtc} removed={Removed}",
            cutoffUtc,
            removed);

        return removed;
    }
}
