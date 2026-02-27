namespace Jellycheckr.Server.Models;

public sealed class SessionState
{
    public string SessionId { get; set; } = string.Empty;
    public DateTimeOffset LastAckUtc { get; set; } = DateTimeOffset.MinValue;
    public DateTimeOffset LastInteractionUtc { get; set; } = DateTimeOffset.MinValue;
    public bool PromptActive { get; set; }
    public DateTimeOffset? PromptDeadlineUtc { get; set; }
    public string? LastItemId { get; set; }
    public int ConsecutiveEpisodesSinceAck { get; set; }
    public DateTimeOffset NextEligiblePromptUtc { get; set; } = DateTimeOffset.MinValue;

    public string? UserId { get; set; }
    public string? UserName { get; set; }
    public string? ClientName { get; set; }
    public string? DeviceName { get; set; }
    public string? DeviceId { get; set; }

    public string? CurrentItemId { get; set; }
    public string? CurrentItemName { get; set; }
    public string? PreviousItemId { get; set; }

    public DateTimeOffset LastSeenUtc { get; set; } = DateTimeOffset.MinValue;
    public long? LastObservedPositionTicks { get; set; }
    public DateTimeOffset? LastPlaybackProgressObservedUtc { get; set; }
    public DateTimeOffset? LastObservedLastActivityDateUtc { get; set; }
    public DateTimeOffset? LastObservedLastPlaybackCheckInUtc { get; set; }
    public DateTimeOffset? LastObservedLastPausedDateUtc { get; set; }
    public DateTimeOffset LastInferredActivityUtc { get; set; } = DateTimeOffset.MinValue;

    public int ServerFallbackEpisodeTransitionsSinceReset { get; set; }
    public long ServerFallbackPlaybackTicksSinceReset { get; set; }
    public bool? IsPaused { get; set; }

    public ServerFallbackPhase FallbackPhase { get; set; } = ServerFallbackPhase.Monitoring;
    public DateTimeOffset? PauseIssuedUtc { get; set; }
    public DateTimeOffset? PauseGraceDeadlineUtc { get; set; }
    public string? LastFallbackAction { get; set; }
    public string? LastFallbackActionResult { get; set; }
    public string? LastFallbackDecisionKey { get; set; }
    public DateTimeOffset? LastFallbackDecisionLoggedUtc { get; set; }
}

public enum ServerFallbackPhase
{
    Monitoring,
    PauseGracePending
}
