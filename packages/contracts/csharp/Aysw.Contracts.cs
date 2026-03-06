using System.Text.Json.Serialization;

namespace Jellycheckr.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EnforcementMode
{
    None,
    WebOnly,
    ServerFallback
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ServerFallbackTriggerMode
{
    Any,
    All
}

public sealed record EffectiveConfigResponse
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }
    [JsonPropertyName("enableEpisodeCheck")]
    public bool EnableEpisodeCheck { get; set; }
    [JsonPropertyName("enableTimerCheck")]
    public bool EnableTimerCheck { get; set; }
    [JsonPropertyName("enableServerFallback")]
    public bool EnableServerFallback { get; set; }
    [JsonPropertyName("episodeThreshold")]
    public int EpisodeThreshold { get; set; }
    [JsonPropertyName("minutesThreshold")]
    public int MinutesThreshold { get; set; }
    [JsonPropertyName("interactionQuietSeconds")]
    public int InteractionQuietSeconds { get; set; }
    [JsonPropertyName("promptTimeoutSeconds")]
    public int PromptTimeoutSeconds { get; set; }
    [JsonPropertyName("cooldownMinutes")]
    public int CooldownMinutes { get; set; }
    [JsonPropertyName("serverFallbackInactivityMinutes")]
    public int ServerFallbackInactivityMinutes { get; set; }
    [JsonPropertyName("serverFallbackPauseBeforeStop")]
    public bool ServerFallbackPauseBeforeStop { get; set; }
    [JsonPropertyName("serverFallbackPauseGraceSeconds")]
    public int ServerFallbackPauseGraceSeconds { get; set; }
    [JsonPropertyName("serverFallbackSendMessageBeforePause")]
    public bool ServerFallbackSendMessageBeforePause { get; set; }
    [JsonPropertyName("clientMessage")]
    public string? ClientMessage { get; set; }
    [JsonPropertyName("serverFallbackDryRun")]
    public bool ServerFallbackDryRun { get; set; }
    [JsonPropertyName("debugLogging")]
    public bool DebugLogging { get; set; }
    [JsonPropertyName("developerMode")]
    public bool DeveloperMode { get; set; }
    [JsonPropertyName("developerPromptAfterSeconds")]
    public int DeveloperPromptAfterSeconds { get; set; }
    [JsonPropertyName("version")]
    public int Version { get; set; }
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; }
}

public sealed record AckRequest
{
    [JsonPropertyName("ackType")]
    public string AckType { get; set; } = "continue";
    [JsonPropertyName("clientTimeUtc")]
    public DateTimeOffset? ClientTimeUtc { get; set; }
    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;
    [JsonPropertyName("itemId")]
    public string? ItemId { get; set; }
    [JsonPropertyName("clientType")]
    public string? ClientType { get; set; }
    [JsonPropertyName("deviceId")]
    public string? DeviceId { get; set; }
}

public sealed record AckResponse
{
    [JsonPropertyName("resetApplied")]
    public bool ResetApplied { get; set; }
    [JsonPropertyName("nextEligiblePromptUtc")]
    public DateTimeOffset NextEligiblePromptUtc { get; set; }
}

public sealed record InteractionRequest
{
    [JsonPropertyName("eventType")]
    public string EventType { get; set; } = string.Empty;
    [JsonPropertyName("clientTimeUtc")]
    public DateTimeOffset? ClientTimeUtc { get; set; }
    [JsonPropertyName("itemId")]
    public string? ItemId { get; set; }
    [JsonPropertyName("clientType")]
    public string? ClientType { get; set; }
    [JsonPropertyName("deviceId")]
    public string? DeviceId { get; set; }
}

public sealed record InteractionResponse
{
    [JsonPropertyName("accepted")]
    public bool Accepted { get; set; }
    [JsonPropertyName("receivedAtUtc")]
    public DateTimeOffset ReceivedAtUtc { get; set; }
}

public sealed record PromptShownRequest
{
    [JsonPropertyName("timeoutSeconds")]
    public int TimeoutSeconds { get; set; }
    [JsonPropertyName("itemId")]
    public string? ItemId { get; set; }
    [JsonPropertyName("clientType")]
    public string? ClientType { get; set; }
    [JsonPropertyName("deviceId")]
    public string? DeviceId { get; set; }
}

public sealed record WebClientRegisterRequest
{
    [JsonPropertyName("deviceId")]
    public string DeviceId { get; set; } = string.Empty;
}

public sealed record WebClientRegisterResponse
{
    [JsonPropertyName("registered")]
    public bool Registered { get; set; }
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }
    [JsonPropertyName("leaseExpiresUtc")]
    public DateTimeOffset? LeaseExpiresUtc { get; set; }
    [JsonPropertyName("config")]
    public EffectiveConfigResponse? Config { get; set; }
}

public sealed record WebClientHeartbeatRequest
{
    [JsonPropertyName("deviceId")]
    public string DeviceId { get; set; } = string.Empty;
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }
}

public sealed record WebClientHeartbeatResponse
{
    [JsonPropertyName("accepted")]
    public bool Accepted { get; set; }
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }
    [JsonPropertyName("leaseExpiresUtc")]
    public DateTimeOffset? LeaseExpiresUtc { get; set; }
}

public sealed record WebClientUnregisterRequest
{
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }
    [JsonPropertyName("deviceId")]
    public string? DeviceId { get; set; }
}
