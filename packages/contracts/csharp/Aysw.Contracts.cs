namespace Jellycheckr.Contracts;

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum EnforcementMode
{
    None,
    WebOnly,
    ServerFallback
}

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum ServerFallbackTriggerMode
{
    Any,
    All
}

public sealed record EffectiveConfigResponse
{
    public bool Enabled { get; set; }
    public bool EnableEpisodeCheck { get; set; }
    public bool EnableTimerCheck { get; set; }
    public bool EnableServerFallback { get; set; }
    public int EpisodeThreshold { get; set; }
    public int MinutesThreshold { get; set; }
    public int InteractionQuietSeconds { get; set; }
    public int PromptTimeoutSeconds { get; set; }
    public int CooldownMinutes { get; set; }
    public int ServerFallbackInactivityMinutes { get; set; }
    public bool ServerFallbackPauseBeforeStop { get; set; }
    public int ServerFallbackPauseGraceSeconds { get; set; }
    public bool ServerFallbackSendMessageBeforePause { get; set; }
    public string? ServerFallbackClientMessage { get; set; }
    public bool ServerFallbackDryRun { get; set; }
    public bool DebugLogging { get; set; }
    public bool DeveloperMode { get; set; }
    public int DeveloperPromptAfterSeconds { get; set; }
    public int Version { get; set; }
    public int SchemaVersion { get; set; }
}

public sealed record AckRequest
{
    public string AckType { get; set; } = "continue";
    public DateTimeOffset? ClientTimeUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? ItemId { get; set; }
    public string? ClientType { get; set; }
    public string? DeviceId { get; set; }
}

public sealed record AckResponse
{
    public bool ResetApplied { get; set; }
    public DateTimeOffset NextEligiblePromptUtc { get; set; }
}

public sealed record InteractionRequest
{
    public string EventType { get; set; } = string.Empty;
    public DateTimeOffset? ClientTimeUtc { get; set; }
    public string? ItemId { get; set; }
    public string? ClientType { get; set; }
    public string? DeviceId { get; set; }
}

public sealed record InteractionResponse
{
    public bool Accepted { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; }
}

public sealed record PromptShownRequest
{
    public int TimeoutSeconds { get; set; }
    public string? ItemId { get; set; }
    public string? ClientType { get; set; }
    public string? DeviceId { get; set; }
}

public sealed record WebClientRegisterRequest
{
    public string DeviceId { get; set; } = string.Empty;
}

public sealed record WebClientRegisterResponse
{
    public bool Registered { get; set; }
    public string? Reason { get; set; }
    public string? SessionId { get; set; }
    public DateTimeOffset? LeaseExpiresUtc { get; set; }
    public EffectiveConfigResponse? Config { get; set; }
}

public sealed record WebClientHeartbeatRequest
{
    public string DeviceId { get; set; } = string.Empty;
    public string? SessionId { get; set; }
}

public sealed record WebClientHeartbeatResponse
{
    public bool Accepted { get; set; }
    public string? Reason { get; set; }
    public string? SessionId { get; set; }
    public DateTimeOffset? LeaseExpiresUtc { get; set; }
}

public sealed record WebClientUnregisterRequest
{
    public string? SessionId { get; set; }
    public string? DeviceId { get; set; }
}
