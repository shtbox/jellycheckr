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

public sealed class EffectiveConfigResponse
{
    public bool Enabled { get; set; }
    public int EpisodeThreshold { get; set; }
    public int MinutesThreshold { get; set; }
    public int InteractionQuietSeconds { get; set; }
    public int PromptTimeoutSeconds { get; set; }
    public int CooldownMinutes { get; set; }
    public EnforcementMode EnforcementMode { get; set; }
    public int ServerFallbackEpisodeThreshold { get; set; }
    public int ServerFallbackMinutesThreshold { get; set; }
    public ServerFallbackTriggerMode ServerFallbackTriggerMode { get; set; }
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

public sealed class AckRequest
{
    public string AckType { get; set; } = "continue";
    public DateTimeOffset? ClientTimeUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? ItemId { get; set; }
    public string? ClientType { get; set; }
    public string? DeviceId { get; set; }
}

public sealed class AckResponse
{
    public bool ResetApplied { get; set; }
    public DateTimeOffset NextEligiblePromptUtc { get; set; }
}

public sealed class InteractionRequest
{
    public string EventType { get; set; } = string.Empty;
    public DateTimeOffset? ClientTimeUtc { get; set; }
    public string? ItemId { get; set; }
    public string? ClientType { get; set; }
    public string? DeviceId { get; set; }
}

public sealed class InteractionResponse
{
    public bool Accepted { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; }
}

public sealed class PromptShownRequest
{
    public int TimeoutSeconds { get; set; }
    public string? ItemId { get; set; }
    public string? ClientType { get; set; }
    public string? DeviceId { get; set; }
}
