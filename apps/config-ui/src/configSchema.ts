import type { PluginConfig } from './types';

export const defaults: PluginConfig = {
  Enabled: true,
  EpisodeThreshold: 3,
  MinutesThreshold: 120,
  InteractionQuietSeconds: 45,
  PromptTimeoutSeconds: 60,
  CooldownMinutes: 30,
  EnforcementMode: 1,
  ServerFallbackEpisodeThreshold: 3,
  ServerFallbackMinutesThreshold: 120,
  ServerFallbackTriggerMode: 0,
  ServerFallbackInactivityMinutes: 30,
  ServerFallbackPauseBeforeStop: true,
  ServerFallbackPauseGraceSeconds: 45,
  ServerFallbackSendMessageBeforePause: true,
  ServerFallbackClientMessage: 'Are you still watching? Playback will stop soon unless you resume.',
  ServerFallbackDryRun: false,
  DebugLogging: false,
  DeveloperMode: false,
  DeveloperPromptAfterSeconds: 15,
  SchemaVersion: 2
};

export function clamp(v: number, min: number, max: number): number {
  if (Number.isNaN(v)) return min;
  return Math.max(min, Math.min(max, v));
}

function parseEnum(
  raw: unknown,
  fallback: number,
  min: number,
  max: number,
  names: Record<string, number>
): number {
  if (typeof raw === 'number') {
    return clamp(raw, min, max);
  }

  if (typeof raw === 'string') {
    const key = raw.trim();
    if (key in names) {
      return clamp(names[key]!, min, max);
    }

    const parsed = parseInt(key, 10);
    if (!Number.isNaN(parsed)) {
      return clamp(parsed, min, max);
    }
  }

  return clamp(fallback, min, max);
}

const enforcementModeNames: Record<string, number> = {
  None: 0,
  WebOnly: 1,
  ServerFallback: 2
};

const serverFallbackTriggerModeNames: Record<string, number> = {
  Any: 0,
  All: 1
};

export function normalize(raw: any): PluginConfig {
  const c = raw || {};

  return {
    Enabled: c.Enabled !== false && c.enabled !== false,
    EpisodeThreshold: clamp(parseInt(c.EpisodeThreshold ?? c.episodeThreshold ?? defaults.EpisodeThreshold, 10), 1, 20),
    MinutesThreshold: clamp(parseInt(c.MinutesThreshold ?? c.minutesThreshold ?? defaults.MinutesThreshold, 10), 1, 600),
    InteractionQuietSeconds: clamp(parseInt(c.InteractionQuietSeconds ?? c.interactionQuietSeconds ?? defaults.InteractionQuietSeconds, 10), 5, 300),
    PromptTimeoutSeconds: clamp(parseInt(c.PromptTimeoutSeconds ?? c.promptTimeoutSeconds ?? defaults.PromptTimeoutSeconds, 10), 10, 300),
    CooldownMinutes: clamp(parseInt(c.CooldownMinutes ?? c.cooldownMinutes ?? defaults.CooldownMinutes, 10), 0, 1440),
    EnforcementMode: parseEnum(
      c.EnforcementMode ?? c.enforcementMode,
      defaults.EnforcementMode,
      0,
      2,
      enforcementModeNames
    ),
    ServerFallbackEpisodeThreshold: clamp(parseInt(c.ServerFallbackEpisodeThreshold ?? c.serverFallbackEpisodeThreshold ?? defaults.ServerFallbackEpisodeThreshold, 10), 0, 20),
    ServerFallbackMinutesThreshold: clamp(parseInt(c.ServerFallbackMinutesThreshold ?? c.serverFallbackMinutesThreshold ?? defaults.ServerFallbackMinutesThreshold, 10), 0, 720),
    ServerFallbackTriggerMode: parseEnum(
      c.ServerFallbackTriggerMode ?? c.serverFallbackTriggerMode,
      defaults.ServerFallbackTriggerMode,
      0,
      1,
      serverFallbackTriggerModeNames
    ),
    ServerFallbackInactivityMinutes: clamp(parseInt(c.ServerFallbackInactivityMinutes ?? c.serverFallbackInactivityMinutes ?? defaults.ServerFallbackInactivityMinutes, 10), 1, 720),
    ServerFallbackPauseBeforeStop: c.ServerFallbackPauseBeforeStop !== false && c.serverFallbackPauseBeforeStop !== false,
    ServerFallbackPauseGraceSeconds: clamp(parseInt(c.ServerFallbackPauseGraceSeconds ?? c.serverFallbackPauseGraceSeconds ?? defaults.ServerFallbackPauseGraceSeconds, 10), 5, 300),
    ServerFallbackSendMessageBeforePause: c.ServerFallbackSendMessageBeforePause !== false && c.serverFallbackSendMessageBeforePause !== false,
    ServerFallbackClientMessage: String(c.ServerFallbackClientMessage ?? c.serverFallbackClientMessage ?? defaults.ServerFallbackClientMessage).trim() || defaults.ServerFallbackClientMessage,
    ServerFallbackDryRun: c.ServerFallbackDryRun === true || c.serverFallbackDryRun === true,
    DebugLogging: c.DebugLogging === true || c.debugLogging === true,
    DeveloperMode: c.DeveloperMode === true || c.developerMode === true,
    DeveloperPromptAfterSeconds: clamp(parseInt(c.DeveloperPromptAfterSeconds ?? c.developerPromptAfterSeconds ?? defaults.DeveloperPromptAfterSeconds, 10), 1, 60),
    SchemaVersion: 2
  };
}
