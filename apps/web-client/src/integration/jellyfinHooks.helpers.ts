import type { EffectiveConfigResponse } from "../../../../packages/contracts/ts/index";
import type { MediaItem } from "../player/playerAdapter";
import type { AyswState } from "../state/ayswStateMachine";
import type { AyswDeveloperHudSnapshot } from "../types/AyswDeveloperHudSnapshot";

export interface PromptThresholdFlags {
  episodeThresholdReached: boolean;
  timeThresholdReached: boolean;
  developerThresholdReached: boolean;
}

interface InitialHudSnapshotArgs {
  sessionId: string;
  mountedAtTs: number;
  config: EffectiveConfigResponse;
  currentItem: MediaItem | null;
  state: AyswState;
  modalVisible: boolean;
}

export function createInitialHudSnapshot(input: InitialHudSnapshotArgs): AyswDeveloperHudSnapshot {
  return {
    moduleState: "mounting",
    sessionId: input.sessionId,
    mountedAtTs: input.mountedAtTs,
    config: input.config,
    currentItem: input.currentItem,
    state: input.state,
    modalVisible: input.modalVisible,
    lastInteraction: {
      eventType: null,
      atTs: null,
      itemId: null,
      sendStatus: "idle",
      sendAtTs: null,
      sendError: null
    },
    lastEvaluation: {
      trigger: null,
      atTs: null,
      promptEligible: null,
      decision: null,
      blockers: [],
      note: null
    },
    lastPrompt: {
      shownAtTs: null,
      shownSendStatus: "idle",
      shownSendAtTs: null,
      shownSendError: null,
      closedAtTs: null,
      closeReason: null
    },
    lastAck: {
      ackType: null,
      status: "idle",
      atTs: null,
      error: null
    },
    lastServerCall: {
      kind: null,
      status: "idle",
      atTs: null,
      note: null,
      error: null
    }
  };
}

export function getPromptBlockers(
  state: AyswState,
  now: number,
  config: EffectiveConfigResponse,
  flags: PromptThresholdFlags
): string[] {
  const blockers: string[] = [];

  if (!config.enabled) {
    blockers.push("disabled");
  }
  if (state.promptOpen) {
    blockers.push("prompt_open");
  }
  if (state.nextEligiblePromptTs > now) {
    blockers.push("cooldown");
  }
  if (!flags.episodeThresholdReached && !flags.timeThresholdReached && !flags.developerThresholdReached) {
    blockers.push("thresholds_not_met");
  }

  return blockers;
}

export function summarizeError(err: unknown): string {
  if (err instanceof Error) {
    return err.message;
  }
  if (typeof err === "string") {
    return err;
  }
  try {
    return JSON.stringify(err);
  } catch {
    return String(err);
  }
}

export function withSafeDefaults(config: EffectiveConfigResponse): EffectiveConfigResponse {
  const raw = config as unknown as Record<string, unknown>;
  const pick = <T>(camelKey: string, pascalKey: string, fallback: T): T => {
    const camelValue = raw[camelKey];
    if (camelValue !== undefined && camelValue !== null) {
      return camelValue as T;
    }

    const pascalValue = raw[pascalKey];
    if (pascalValue !== undefined && pascalValue !== null) {
      return pascalValue as T;
    }

    return fallback;
  };

  const pickNumber = (camelKey: string, pascalKey: string, fallback: number): number => {
    const value = Number(pick(camelKey, pascalKey, fallback));
    return Number.isFinite(value) ? value : fallback;
  };

  const legacyEnforcementMode = String(pick("enforcementMode", "EnforcementMode", "WebOnly"));

  return {
    enabled: pick("enabled", "Enabled", true),
    enableEpisodeCheck: Boolean(pick("enableEpisodeCheck", "EnableEpisodeCheck", true)),
    enableTimerCheck: Boolean(pick("enableTimerCheck", "EnableTimerCheck", true)),
    enableServerFallback: Boolean(
      pick("enableServerFallback", "EnableServerFallback", legacyEnforcementMode === "ServerFallback")
    ),
    episodeThreshold: Math.max(pickNumber("episodeThreshold", "EpisodeThreshold", 3), 1),
    minutesThreshold: Math.max(pickNumber("minutesThreshold", "MinutesThreshold", 120), 1),
    interactionQuietSeconds: Math.max(pickNumber("interactionQuietSeconds", "InteractionQuietSeconds", 45), 5),
    promptTimeoutSeconds: Math.max(pickNumber("promptTimeoutSeconds", "PromptTimeoutSeconds", 60), 10),
    cooldownMinutes: Math.max(pickNumber("cooldownMinutes", "CooldownMinutes", 30), 0),
    serverFallbackInactivityMinutes: Math.max(
      pickNumber("serverFallbackInactivityMinutes", "ServerFallbackInactivityMinutes", 30),
      1
    ),
    serverFallbackPauseBeforeStop: Boolean(
      pick("serverFallbackPauseBeforeStop", "ServerFallbackPauseBeforeStop", true)
    ),
    serverFallbackPauseGraceSeconds: Math.max(
      pickNumber("serverFallbackPauseGraceSeconds", "ServerFallbackPauseGraceSeconds", 45),
      5
    ),
    serverFallbackSendMessageBeforePause: Boolean(
      pick("serverFallbackSendMessageBeforePause", "ServerFallbackSendMessageBeforePause", true)
    ),
    clientMessage: String(
      pick(
        "clientMessage",
        "ServerFallbackClientMessage",
        "Are you still watching? Playback will stop soon unless you resume."
      ) ?? "Are you still watching? Playback will stop soon unless you resume."
    ),
    serverFallbackDryRun: Boolean(pick("serverFallbackDryRun", "ServerFallbackDryRun", false)),
    debugLogging: Boolean(pick("debugLogging", "DebugLogging", false)),
    developerMode: Boolean(pick("developerMode", "DeveloperMode", false)),
    developerPromptAfterSeconds: Math.max(
      pickNumber("developerPromptAfterSeconds", "DeveloperPromptAfterSeconds", 15),
      1
    ),
    version: Math.max(pickNumber("version", "Version", pickNumber("schemaVersion", "SchemaVersion", 1)), 1),
    schemaVersion: Math.max(pickNumber("schemaVersion", "SchemaVersion", 1), 1)
  };
}
