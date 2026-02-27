import type { PluginConfig } from './types';

export const defaults: PluginConfig = {
  Enabled: true,
  EnableEpisodeCheck: true,
  EnableTimerCheck: true,
  EnableServerFallback: true,
  EpisodeThreshold: 3,
  MinutesThreshold: 120,
  InteractionQuietSeconds: 45,
  PromptTimeoutSeconds: 60,
  CooldownMinutes: 30,
  ServerFallbackInactivityMinutes: 30,
  ServerFallbackPauseBeforeStop: true,
  ServerFallbackPauseGraceSeconds: 45,
  ServerFallbackSendMessageBeforePause: true,
  ServerFallbackClientMessage: 'Are you still watching? Playback will stop soon unless you resume.',
  ServerFallbackDryRun: false,
  MinimumLogLevel: 3,
  DebugLogging: false,
  DeveloperMode: false,
  DeveloperPromptAfterSeconds: 15,
  SchemaVersion: 3
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

const logLevelNames: Record<string, number> = {
  Trace: 0,
  Debug: 1,
  Information: 2,
  Warning: 3,
  Error: 4,
  Critical: 5,
  None: 6
};

export function normalize(raw: any): PluginConfig {
  const c = raw || {};
  const legacyMode = parseEnum(
    c.EnforcementMode ?? c.enforcementMode,
    1,
    0,
    2,
    enforcementModeNames
  );
  const hasEpisodeToggle = c.EnableEpisodeCheck !== undefined || c.enableEpisodeCheck !== undefined;
  const hasTimerToggle = c.EnableTimerCheck !== undefined || c.enableTimerCheck !== undefined;
  const hasFallbackToggle = c.EnableServerFallback !== undefined || c.enableServerFallback !== undefined;
  const hasEpisodeThreshold = c.EpisodeThreshold !== undefined || c.episodeThreshold !== undefined;
  const hasMinutesThreshold = c.MinutesThreshold !== undefined || c.minutesThreshold !== undefined;

  const legacyFallbackEpisodeThreshold = clamp(
    parseInt(c.ServerFallbackEpisodeThreshold ?? c.serverFallbackEpisodeThreshold ?? defaults.EpisodeThreshold, 10),
    0,
    20
  );
  const legacyFallbackMinutesThreshold = clamp(
    parseInt(c.ServerFallbackMinutesThreshold ?? c.serverFallbackMinutesThreshold ?? defaults.MinutesThreshold, 10),
    0,
    720
  );

  let enableEpisodeCheck = c.EnableEpisodeCheck !== false && c.enableEpisodeCheck !== false;
  let enableTimerCheck = c.EnableTimerCheck !== false && c.enableTimerCheck !== false;
  let enableServerFallback = c.EnableServerFallback !== false && c.enableServerFallback !== false;
  let episodeThreshold = clamp(parseInt(c.EpisodeThreshold ?? c.episodeThreshold ?? defaults.EpisodeThreshold, 10), 1, 20);
  let minutesThreshold = clamp(parseInt(c.MinutesThreshold ?? c.minutesThreshold ?? defaults.MinutesThreshold, 10), 1, 600);

  if (!hasFallbackToggle) {
    enableServerFallback = legacyMode == 2;
  }

  if (legacyMode == 2) {
    if (!hasEpisodeToggle) {
      enableEpisodeCheck = legacyFallbackEpisodeThreshold > 0;
    }
    if (!hasTimerToggle) {
      enableTimerCheck = legacyFallbackMinutesThreshold > 0;
    }
    if (!hasEpisodeThreshold && legacyFallbackEpisodeThreshold > 0) {
      episodeThreshold = legacyFallbackEpisodeThreshold;
    }
    if (!hasMinutesThreshold && legacyFallbackMinutesThreshold > 0) {
      minutesThreshold = Math.min(600, legacyFallbackMinutesThreshold);
    }
  }

  if (!enableEpisodeCheck && !enableTimerCheck) {
    enableEpisodeCheck = true;
    enableTimerCheck = true;
  }

  return {
    Enabled: c.Enabled !== false && c.enabled !== false,
    EnableEpisodeCheck: enableEpisodeCheck,
    EnableTimerCheck: enableTimerCheck,
    EnableServerFallback: enableServerFallback,
    EpisodeThreshold: episodeThreshold,
    MinutesThreshold: minutesThreshold,
    InteractionQuietSeconds: clamp(parseInt(c.InteractionQuietSeconds ?? c.interactionQuietSeconds ?? defaults.InteractionQuietSeconds, 10), 5, 300),
    PromptTimeoutSeconds: clamp(parseInt(c.PromptTimeoutSeconds ?? c.promptTimeoutSeconds ?? defaults.PromptTimeoutSeconds, 10), 10, 300),
    CooldownMinutes: clamp(parseInt(c.CooldownMinutes ?? c.cooldownMinutes ?? defaults.CooldownMinutes, 10), 0, 1440),
    ServerFallbackInactivityMinutes: clamp(parseInt(c.ServerFallbackInactivityMinutes ?? c.serverFallbackInactivityMinutes ?? defaults.ServerFallbackInactivityMinutes, 10), 1, 720),
    ServerFallbackPauseBeforeStop: c.ServerFallbackPauseBeforeStop !== false && c.serverFallbackPauseBeforeStop !== false,
    ServerFallbackPauseGraceSeconds: clamp(parseInt(c.ServerFallbackPauseGraceSeconds ?? c.serverFallbackPauseGraceSeconds ?? defaults.ServerFallbackPauseGraceSeconds, 10), 5, 300),
    ServerFallbackSendMessageBeforePause: c.ServerFallbackSendMessageBeforePause !== false && c.serverFallbackSendMessageBeforePause !== false,
    ServerFallbackClientMessage: String(c.ServerFallbackClientMessage ?? c.serverFallbackClientMessage ?? defaults.ServerFallbackClientMessage).trim() || defaults.ServerFallbackClientMessage,
    ServerFallbackDryRun: c.ServerFallbackDryRun === true || c.serverFallbackDryRun === true,
    MinimumLogLevel: parseEnum(
      c.MinimumLogLevel ?? c.minimumLogLevel,
      defaults.MinimumLogLevel,
      0,
      6,
      logLevelNames
    ),
    DebugLogging: c.DebugLogging === true || c.debugLogging === true,
    DeveloperMode: c.DeveloperMode === true || c.developerMode === true,
    DeveloperPromptAfterSeconds: clamp(parseInt(c.DeveloperPromptAfterSeconds ?? c.developerPromptAfterSeconds ?? defaults.DeveloperPromptAfterSeconds, 10), 1, 60),
    SchemaVersion: 3
  };
}
