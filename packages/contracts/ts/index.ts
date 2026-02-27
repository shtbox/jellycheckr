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
  serverFallbackClientMessage?: string | null;
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
