export type EnforcementMode = "None" | "WebOnly" | "ServerFallback";
export type ServerFallbackTriggerMode = "Any" | "All";

export interface EffectiveConfigResponse {
  enabled: boolean;
  enableEpisodeCheck: boolean;
  enableTimerCheck: boolean;
  enableServerFallback: boolean;
  episodeThreshold: number;
  minutesThreshold: number;
  interactionQuietSeconds: number;
  promptTimeoutSeconds: number;
  cooldownMinutes: number;
  serverFallbackInactivityMinutes: number;
  serverFallbackPauseBeforeStop: boolean;
  serverFallbackPauseGraceSeconds: number;
  serverFallbackSendMessageBeforePause: boolean;
  clientMessage?: string | null;
  serverFallbackDryRun: boolean;
  debugLogging: boolean;
  developerMode: boolean;
  developerPromptAfterSeconds: number;
  version: number;
  schemaVersion: number;
}

export interface AckRequest {
  ackType: "continue" | "stop";
  clientTimeUtc?: string;
  reason: string;
  itemId?: string;
  clientType?: string;
  deviceId?: string;
}

export interface AckResponse {
  resetApplied: boolean;
  nextEligiblePromptUtc: string;
}

export interface InteractionRequest {
  eventType: string;
  clientTimeUtc?: string;
  itemId?: string;
  clientType?: string;
  deviceId?: string;
}

export interface InteractionResponse {
  accepted: boolean;
  receivedAtUtc: string;
}

export interface PromptShownRequest {
  timeoutSeconds: number;
  itemId?: string;
  clientType?: string;
  deviceId?: string;
}

export interface WebClientRegisterRequest {
  deviceId: string;
}

export interface WebClientRegisterResponse {
  registered: boolean;
  reason?: string | null;
  sessionId?: string | null;
  leaseExpiresUtc?: string | null;
  config?: EffectiveConfigResponse | null;
}

export interface WebClientHeartbeatRequest {
  deviceId: string;
  sessionId?: string;
}

export interface WebClientHeartbeatResponse {
  accepted: boolean;
  reason?: string | null;
  sessionId?: string | null;
  leaseExpiresUtc?: string | null;
}

export interface WebClientUnregisterRequest {
  sessionId?: string;
  deviceId?: string;
}
