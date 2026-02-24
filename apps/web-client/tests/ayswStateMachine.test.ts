import { describe, expect, it } from "vitest";
import type { EffectiveConfigResponse } from "../../../packages/contracts/ts/index";
import {
  closePromptFromAck,
  createInitialState,
  openPrompt,
  registerInteraction,
  registerItemTransition,
  shouldPrompt
} from "../src/state/ayswStateMachine";

const config: EffectiveConfigResponse = {
  enabled: true,
  episodeThreshold: 2,
  minutesThreshold: 120,
  interactionQuietSeconds: 45,
  promptTimeoutSeconds: 60,
  cooldownMinutes: 30,
  enforcementMode: "WebOnly",
  debugLogging: false,
  developerMode: false,
  developerPromptAfterSeconds: 15,
  version: 1,
  schemaVersion: 1
};

describe("aysw state machine", () => {
  it("prompts after enough episode transitions without recent interaction", () => {
    let state = createInitialState(1_000);
    state = { ...state, lastItem: { id: "e1", type: "Episode", seriesId: "s" }, lastInteractionTs: 0 };

    state = registerItemTransition(state, 60_000, { id: "e2", type: "Episode", seriesId: "s" }, config);
    state = registerItemTransition(state, 120_000, { id: "e3", type: "Episode", seriesId: "s" }, config);

    expect(shouldPrompt(state, 120_000, config)).toBe(true);
  });

  it("continue ack closes prompt and applies cooldown", () => {
    let state = createInitialState(0);
    state = openPrompt(state, 10_000, config);
    state = closePromptFromAck(state, 20_000, config.cooldownMinutes);
    expect(state.promptOpen).toBe(false);
    expect(state.episodeTransitionsSinceAck).toBe(0);
    expect(state.nextEligiblePromptTs).toBeGreaterThan(20_000);
  });

  it("interaction updates last interaction timestamp", () => {
    let state = createInitialState(0);
    state = registerInteraction(state, 25_000);
    expect(state.lastInteractionTs).toBe(25_000);
  });

  it("developer mode prompts quickly after configured seconds", () => {
    const devConfig: EffectiveConfigResponse = {
      ...config,
      developerMode: true,
      developerPromptAfterSeconds: 5
    };
    const state = createInitialState(0);
    expect(shouldPrompt(state, 4_000, devConfig)).toBe(false);
    expect(shouldPrompt(state, 5_000, devConfig)).toBe(true);
  });
});
