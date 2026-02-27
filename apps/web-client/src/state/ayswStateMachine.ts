import type { EffectiveConfigResponse } from "../../../../packages/contracts/ts/index";
import type { MediaItem } from "../player/playerAdapter";

export interface AyswState {
  lastInteractionTs: number;
  lastPromptResetTs: number;
  episodeTransitionsSinceAck: number;
  promptOpen: boolean;
  promptDeadlineTs: number | null;
  nextEligiblePromptTs: number;
  lastItem: MediaItem | null;
}

export function createInitialState(nowTs: number): AyswState {
  return {
    lastInteractionTs: nowTs,
    lastPromptResetTs: nowTs,
    episodeTransitionsSinceAck: 0,
    promptOpen: false,
    promptDeadlineTs: null,
    nextEligiblePromptTs: 0,
    lastItem: null
  };
}

export function registerInteraction(state: AyswState, nowTs: number): AyswState {
  return { ...state, lastInteractionTs: nowTs };
}

export function registerItemTransition(
  state: AyswState,
  nowTs: number,
  nextItem: MediaItem | null,
  config: EffectiveConfigResponse
): AyswState {
  if (!nextItem || !state.lastItem) {
    return { ...state, lastItem: nextItem };
  }

  const bothEpisodes =
    normalizeType(nextItem.type) === "episode" && normalizeType(state.lastItem.type) === "episode";
  const sameSeries = !!nextItem.seriesId && nextItem.seriesId === state.lastItem.seriesId;
  const interactedRecently = nowTs - state.lastInteractionTs <= config.interactionQuietSeconds * 1000;

  if (bothEpisodes && sameSeries && !interactedRecently) {
    return {
      ...state,
      episodeTransitionsSinceAck: state.episodeTransitionsSinceAck + 1,
      lastItem: nextItem
    };
  }

  return { ...state, lastItem: nextItem };
}

export function shouldPrompt(
  state: AyswState,
  nowTs: number,
  config: EffectiveConfigResponse
): boolean {
  if (!config.enabled || state.promptOpen) {
    return false;
  }

  if (state.nextEligiblePromptTs > nowTs) {
    return false;
  }

  const episodeThresholdReached =
    config.enableEpisodeCheck && state.episodeTransitionsSinceAck >= config.episodeThreshold;
  const minutesWithoutInteraction = (nowTs - state.lastInteractionTs) / 60000;
  const timeThresholdReached = config.enableTimerCheck && minutesWithoutInteraction >= config.minutesThreshold;
  const developerThresholdReached =
    config.developerMode &&
    nowTs - state.lastPromptResetTs >= config.developerPromptAfterSeconds * 1000;

  return developerThresholdReached || episodeThresholdReached || timeThresholdReached;
}

export function openPrompt(state: AyswState, nowTs: number, config: EffectiveConfigResponse): AyswState {
  return {
    ...state,
    promptOpen: true,
    promptDeadlineTs: nowTs + config.promptTimeoutSeconds * 1000
  };
}

export function closePromptFromAck(state: AyswState, nowTs: number, cooldownMinutes: number): AyswState {
  return {
    ...state,
    promptOpen: false,
    promptDeadlineTs: null,
    episodeTransitionsSinceAck: 0,
    nextEligiblePromptTs: nowTs + cooldownMinutes * 60_000,
    lastPromptResetTs: nowTs,
    lastInteractionTs: nowTs
  };
}

function normalizeType(type: string | undefined): string {
  return (type ?? "").toLowerCase();
}
