import type { AckRequest, EffectiveConfigResponse } from "../../../../packages/contracts/ts/index";
import { HttpApiClient } from "../api/client";
import { debug, setDebugLogging, warn } from "../logging/logger";
import type { PlayerAdapter } from "../player/playerAdapter";
import {
  closePromptFromAck,
  createInitialState,
  openPrompt,
  registerInteraction,
  registerItemTransition,
  shouldPrompt,
  type AyswState
} from "../state/ayswStateMachine";
import {
  createAyswDeveloperHud,
  type AyswDeveloperHudSnapshot,
  type HudTransportStatus
} from "../ui/developerHud";
import { createModalController } from "../ui/modal";

export interface AyswModule {
  dispose(): void;
}

export async function mountAysw(player: PlayerAdapter): Promise<AyswModule> {
  const api = new HttpApiClient();
  let config: EffectiveConfigResponse;
  try {
    config = withSafeDefaults(await api.getEffectiveConfig());
  } catch (err) {
    warn("Failed to load effective config", err);
    throw err;
  }
  setDebugLogging(config.debugLogging || config.developerMode);
  const sessionId = player.getSessionId();
  const modal = createModalController();
  let state = createInitialState(Date.now());
  let lastMouseMoveTs = 0;
  let modalVisible = false;

  const devHud = config.developerMode ? createAyswDeveloperHud() : null;
  const hudSnapshot: AyswDeveloperHudSnapshot | null = devHud
    ? {
        moduleState: "mounting",
        sessionId,
        mountedAtTs: Date.now(),
        config,
        currentItem: player.getCurrentItem(),
        state,
        modalVisible,
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
      }
    : null;

  const renderHud = (): void => {
    if (!devHud || !hudSnapshot) {
      return;
    }

    hudSnapshot.currentItem = player.getCurrentItem();
    hudSnapshot.state = state;
    hudSnapshot.modalVisible = modalVisible;
    devHud.render(hudSnapshot);
  };

  const pushHudEvent = (message: string): void => {
    if (!devHud) {
      return;
    }
    devHud.pushEvent(message);
  };

  const setHudServerCall = (
    kind: string,
    status: HudTransportStatus,
    note: string | null = null,
    err: unknown = null
  ): void => {
    if (!hudSnapshot) {
      return;
    }

    hudSnapshot.lastServerCall.kind = kind;
    hudSnapshot.lastServerCall.status = status;
    hudSnapshot.lastServerCall.atTs = Date.now();
    hudSnapshot.lastServerCall.note = note;
    hudSnapshot.lastServerCall.error = err ? summarizeError(err) : null;
  };

  const setHudPromptClosed = (reason: string): void => {
    if (!hudSnapshot) {
      return;
    }

    hudSnapshot.lastPrompt.closedAtTs = Date.now();
    hudSnapshot.lastPrompt.closeReason = reason;
  };

  debug("Mounting AYSW module", {
    sessionId,
    config: {
      enabled: config.enabled,
      episodeThreshold: config.episodeThreshold,
      minutesThreshold: config.minutesThreshold,
      interactionQuietSeconds: config.interactionQuietSeconds,
      promptTimeoutSeconds: config.promptTimeoutSeconds,
      cooldownMinutes: config.cooldownMinutes,
      enforcementMode: config.enforcementMode,
      debugLogging: config.debugLogging,
      developerMode: config.developerMode
    }
  });

  renderHud();
  pushHudEvent("module starting");

  const interactionHandler = async (eventType: string): Promise<void> => {
    const now = Date.now();
    if (eventType === "mousemove" && now - lastMouseMoveTs < 3000) {
      return;
    }
    if (eventType === "mousemove") {
      lastMouseMoveTs = now;
    }

    const currentItem = player.getCurrentItem();
    state = registerInteraction(state, now);
    if (hudSnapshot) {
      hudSnapshot.lastInteraction.eventType = eventType;
      hudSnapshot.lastInteraction.atTs = now;
      hudSnapshot.lastInteraction.itemId = currentItem?.id ?? null;
      hudSnapshot.lastInteraction.sendStatus = "pending";
      hudSnapshot.lastInteraction.sendAtTs = null;
      hudSnapshot.lastInteraction.sendError = null;
    }
    setHudServerCall("interaction", "pending", eventType);
    renderHud();

    debug("Recorded interaction", {
      eventType,
      itemId: currentItem?.id
    });
    try {
      await api.sendInteraction(sessionId, {
        eventType,
        clientTimeUtc: new Date(now).toISOString(),
        itemId: currentItem?.id,
        clientType: "web"
      });
      if (hudSnapshot) {
        hudSnapshot.lastInteraction.sendStatus = "ok";
        hudSnapshot.lastInteraction.sendAtTs = Date.now();
      }
      setHudServerCall("interaction", "ok", eventType);
      renderHud();
      if (eventType !== "mousemove") {
        pushHudEvent(`interaction sent (${eventType})`);
      }
      debug("Sent interaction to server", { eventType });
    } catch (err) {
      if (hudSnapshot) {
        hudSnapshot.lastInteraction.sendStatus = "error";
        hudSnapshot.lastInteraction.sendAtTs = Date.now();
        hudSnapshot.lastInteraction.sendError = summarizeError(err);
      }
      setHudServerCall("interaction", "error", eventType, err);
      renderHud();
      if (eventType !== "mousemove") {
        pushHudEvent(`interaction failed (${eventType})`);
      }
      debug("Failed to send interaction (non-fatal)", { eventType, err });
    }
  };

  const evaluate = async (trigger: string): Promise<void> => {
    const now = Date.now();
    const currentItem = player.getCurrentItem();
    state = registerItemTransition(state, now, currentItem, config);
    const minutesWithoutInteraction = (now - state.lastInteractionTs) / 60000;
    const episodeThresholdReached = state.episodeTransitionsSinceAck >= config.episodeThreshold;
    const timeThresholdReached = minutesWithoutInteraction >= config.minutesThreshold;
    const developerThresholdReached =
      Boolean(config.developerMode) &&
      now - state.lastPromptResetTs >= config.developerPromptAfterSeconds * 1000;
    const promptEligible = shouldPrompt(state, now, config);
    const promptBlockers = getPromptBlockers(state, now, config, {
      episodeThresholdReached,
      timeThresholdReached,
      developerThresholdReached
    });

    if (hudSnapshot) {
      hudSnapshot.lastEvaluation.trigger = trigger;
      hudSnapshot.lastEvaluation.atTs = now;
      hudSnapshot.lastEvaluation.promptEligible = promptEligible;
      hudSnapshot.lastEvaluation.decision = promptEligible ? "open_prompt" : "skip";
      hudSnapshot.lastEvaluation.blockers = promptEligible ? [] : promptBlockers;
      hudSnapshot.lastEvaluation.note = promptEligible
        ? "threshold reached"
        : promptBlockers.join(", ");
    }
    renderHud();
    pushHudEvent(
      promptEligible
        ? `eval ${trigger} -> open prompt`
        : `eval ${trigger} -> skip (${promptBlockers.join(", ") || "not eligible"})`
    );

    debug("Evaluate prompt conditions", {
      trigger,
      sessionId,
      nowIso: new Date(now).toISOString(),
      currentItem,
      promptEligible,
      state: {
        promptOpen: state.promptOpen,
        promptDeadlineTs: state.promptDeadlineTs,
        lastInteractionTs: state.lastInteractionTs,
        lastPromptResetTs: state.lastPromptResetTs,
        nextEligiblePromptTs: state.nextEligiblePromptTs,
        episodeTransitionsSinceAck: state.episodeTransitionsSinceAck,
        lastItem: state.lastItem
      },
      thresholds: {
        episodeThresholdReached,
        timeThresholdReached,
        developerThresholdReached,
        minutesWithoutInteraction,
        minutesThreshold: config.minutesThreshold,
        developerMode: config.developerMode,
        developerPromptAfterSeconds: config.developerPromptAfterSeconds
      }
    });

    if (!promptEligible) {
      return;
    }

    state = openPrompt(state, now, config);
    if (hudSnapshot) {
      hudSnapshot.lastPrompt.shownAtTs = null;
      hudSnapshot.lastPrompt.shownSendStatus = "pending";
      hudSnapshot.lastPrompt.shownSendAtTs = null;
      hudSnapshot.lastPrompt.shownSendError = null;
      hudSnapshot.lastPrompt.closedAtTs = null;
      hudSnapshot.lastPrompt.closeReason = null;
    }
    setHudServerCall("prompt-shown", "pending", currentItem?.id ?? null);
    renderHud();

    debug("Opening prompt", {
      itemId: currentItem?.id,
      trigger,
      timeoutSeconds: config.promptTimeoutSeconds
    });
    try {
      await api.sendPromptShown(sessionId, {
        timeoutSeconds: config.promptTimeoutSeconds,
        itemId: currentItem?.id,
        clientType: "web"
      });
      if (hudSnapshot) {
        hudSnapshot.lastPrompt.shownSendStatus = "ok";
        hudSnapshot.lastPrompt.shownSendAtTs = Date.now();
      }
      setHudServerCall("prompt-shown", "ok", currentItem?.id ?? null);
      renderHud();
      debug("Sent prompt-shown event", { itemId: currentItem?.id });
    } catch (err) {
      if (hudSnapshot) {
        hudSnapshot.lastPrompt.shownSendStatus = "error";
        hudSnapshot.lastPrompt.shownSendAtTs = Date.now();
        hudSnapshot.lastPrompt.shownSendError = summarizeError(err);
      }
      setHudServerCall("prompt-shown", "error", currentItem?.id ?? null, err);
      renderHud();
      pushHudEvent("prompt-shown send failed");
      debug("Failed to send prompt-shown event (non-fatal)", { itemId: currentItem?.id, err });
    }

    modalVisible = true;
    if (hudSnapshot) {
      hudSnapshot.lastPrompt.shownAtTs = Date.now();
      hudSnapshot.lastPrompt.closedAtTs = null;
      hudSnapshot.lastPrompt.closeReason = null;
    }
    renderHud();
    pushHudEvent(`prompt visible (${trigger})`);

    modal.show(
      async () => {
        debug("Prompt action selected", { action: "continue" });

        if (hudSnapshot) {
          hudSnapshot.lastAck.ackType = "continue";
          hudSnapshot.lastAck.status = "pending";
          hudSnapshot.lastAck.atTs = Date.now();
          hudSnapshot.lastAck.error = null;
        }
        setHudServerCall("ack", "pending", "continue");
        renderHud();
        pushHudEvent("prompt action continue");

        const ack: AckRequest = {
          ackType: "continue",
          clientTimeUtc: new Date().toISOString(),
          reason: "user_clicked_continue",
          itemId: player.getCurrentItem()?.id,
          clientType: "web"
        };
        try {
          await api.sendAck(sessionId, ack);
          if (hudSnapshot) {
            hudSnapshot.lastAck.status = "ok";
            hudSnapshot.lastAck.atTs = Date.now();
          }
          setHudServerCall("ack", "ok", ack.ackType);
          renderHud();
          pushHudEvent("ack sent (continue)");
          debug("Sent ack", { ackType: ack.ackType });
        } catch (err) {
          if (hudSnapshot) {
            hudSnapshot.lastAck.status = "error";
            hudSnapshot.lastAck.atTs = Date.now();
            hudSnapshot.lastAck.error = summarizeError(err);
          }
          setHudServerCall("ack", "error", ack.ackType, err);
          renderHud();
          pushHudEvent("ack failed (continue)");
          debug("Failed to send ack (non-fatal)", { ackType: ack.ackType, err });
        }

        state = closePromptFromAck(state, Date.now(), config.cooldownMinutes);
        modal.close();
        modalVisible = false;
        setHudPromptClosed("continue");
        renderHud();
        pushHudEvent("prompt closed (continue)");
      },
      async () => {
        debug("Prompt action selected", { action: "stop" });
        player.stopPlayback();

        if (hudSnapshot) {
          hudSnapshot.lastAck.ackType = "stop";
          hudSnapshot.lastAck.status = "pending";
          hudSnapshot.lastAck.atTs = Date.now();
          hudSnapshot.lastAck.error = null;
        }
        setHudServerCall("ack", "pending", "stop");
        renderHud();
        pushHudEvent("prompt action stop");

        const ack: AckRequest = {
          ackType: "stop",
          clientTimeUtc: new Date().toISOString(),
          reason: "timeout_or_user_stop",
          itemId: player.getCurrentItem()?.id,
          clientType: "web"
        };
        try {
          await api.sendAck(sessionId, ack);
          if (hudSnapshot) {
            hudSnapshot.lastAck.status = "ok";
            hudSnapshot.lastAck.atTs = Date.now();
          }
          setHudServerCall("ack", "ok", ack.ackType);
          renderHud();
          pushHudEvent("ack sent (stop)");
          debug("Sent ack", { ackType: ack.ackType });
        } catch (err) {
          if (hudSnapshot) {
            hudSnapshot.lastAck.status = "error";
            hudSnapshot.lastAck.atTs = Date.now();
            hudSnapshot.lastAck.error = summarizeError(err);
          }
          setHudServerCall("ack", "error", ack.ackType, err);
          renderHud();
          pushHudEvent("ack failed (stop)");
          debug("Failed to send ack (non-fatal)", { ackType: ack.ackType, err });
        }

        state = closePromptFromAck(state, Date.now(), config.cooldownMinutes);
        modal.close();
        modalVisible = false;
        setHudPromptClosed("stop");
        renderHud();
        pushHudEvent("prompt closed (stop)");
        try {
          player.exitPlaybackView?.();
        } catch (err) {
          pushHudEvent("exit playback failed");
          debug("Failed to exit playback view after stop action (non-fatal)", { err });
        }
      },
      config.promptTimeoutSeconds
    );
  };

  const disposers: Array<() => void> = [];
  const domEvents = ["keydown", "pointerdown", "touchstart", "mousemove"];
  debug("Binding interaction listeners", { domEvents });
  domEvents.forEach((name) => {
    const handler = (): void => void interactionHandler(name);
    window.addEventListener(name, handler, { passive: true });
    disposers.push(() => window.removeEventListener(name, handler));
  });

  disposers.push(player.on("itemchange", () => void evaluate("itemchange")));
  disposers.push(player.on("seek", () => void interactionHandler("seek")));
  disposers.push(player.on("pause", () => void interactionHandler("pause")));
  disposers.push(player.on("menuopen", () => void interactionHandler("menuopen")));

  const intervalId = window.setInterval(() => void evaluate("interval"), 15_000);
  disposers.push(() => window.clearInterval(intervalId));

  const pageHideHandler = (): void => {
    modal.close();
    if (modalVisible) {
      modalVisible = false;
      setHudPromptClosed("pagehide");
      renderHud();
      pushHudEvent("prompt UI closed (pagehide)");
    }
  };
  window.addEventListener("pagehide", pageHideHandler);
  disposers.push(() => window.removeEventListener("pagehide", pageHideHandler));

  const visibilityHandler = (): void => {
    if (document.visibilityState === "hidden") {
      debug("Closing prompt due to document hidden state");
      modal.close();
      if (modalVisible) {
        modalVisible = false;
        setHudPromptClosed("hidden");
        renderHud();
        pushHudEvent("prompt UI closed (hidden)");
      }
    }
  };
  document.addEventListener("visibilitychange", visibilityHandler);
  disposers.push(() => document.removeEventListener("visibilitychange", visibilityHandler));

  if (hudSnapshot) {
    hudSnapshot.moduleState = "mounted";
  }
  renderHud();
  pushHudEvent("module mounted");

  void evaluate("mount");
  debug("AYSW module mounted");

  return {
    dispose(): void {
      if (hudSnapshot) {
        hudSnapshot.moduleState = "disposing";
      }
      modalVisible = false;
      renderHud();
      pushHudEvent("module disposing");

      debug("Disposing AYSW module");
      disposers.forEach((d) => d());
      modal.dispose();
      devHud?.dispose();
    }
  };
}

interface PromptThresholdFlags {
  episodeThresholdReached: boolean;
  timeThresholdReached: boolean;
  developerThresholdReached: boolean;
}

function getPromptBlockers(
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

function summarizeError(err: unknown): string {
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

function withSafeDefaults(config: EffectiveConfigResponse): EffectiveConfigResponse {
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

  return {
    enabled: pick("enabled", "Enabled", true),
    episodeThreshold: Math.max(pickNumber("episodeThreshold", "EpisodeThreshold", 3), 1),
    minutesThreshold: Math.max(pickNumber("minutesThreshold", "MinutesThreshold", 120), 1),
    interactionQuietSeconds: Math.max(pickNumber("interactionQuietSeconds", "InteractionQuietSeconds", 45), 5),
    promptTimeoutSeconds: Math.max(pickNumber("promptTimeoutSeconds", "PromptTimeoutSeconds", 60), 10),
    cooldownMinutes: Math.max(pickNumber("cooldownMinutes", "CooldownMinutes", 30), 0),
    enforcementMode: (pick("enforcementMode", "EnforcementMode", "WebOnly") as any) ?? "WebOnly",
    serverFallbackEpisodeThreshold: Math.max(pickNumber("serverFallbackEpisodeThreshold", "ServerFallbackEpisodeThreshold", 3), 0),
    serverFallbackMinutesThreshold: Math.max(pickNumber("serverFallbackMinutesThreshold", "ServerFallbackMinutesThreshold", 120), 0),
    serverFallbackTriggerMode: (pick("serverFallbackTriggerMode", "ServerFallbackTriggerMode", "Any") as any) ?? "Any",
    serverFallbackInactivityMinutes: Math.max(pickNumber("serverFallbackInactivityMinutes", "ServerFallbackInactivityMinutes", 30), 1),
    serverFallbackPauseBeforeStop: Boolean(pick("serverFallbackPauseBeforeStop", "ServerFallbackPauseBeforeStop", true)),
    serverFallbackPauseGraceSeconds: Math.max(pickNumber("serverFallbackPauseGraceSeconds", "ServerFallbackPauseGraceSeconds", 45), 5),
    serverFallbackSendMessageBeforePause: Boolean(pick("serverFallbackSendMessageBeforePause", "ServerFallbackSendMessageBeforePause", true)),
    serverFallbackClientMessage: String(
      pick(
        "serverFallbackClientMessage",
        "ServerFallbackClientMessage",
        "Are you still watching? Playback will stop soon unless you resume."
      ) ?? "Are you still watching? Playback will stop soon unless you resume."
    ),
    serverFallbackDryRun: Boolean(pick("serverFallbackDryRun", "ServerFallbackDryRun", false)),
    debugLogging: Boolean(pick("debugLogging", "DebugLogging", false)),
    developerMode: Boolean(pick("developerMode", "DeveloperMode", false)),
    developerPromptAfterSeconds: Math.max(pickNumber("developerPromptAfterSeconds", "DeveloperPromptAfterSeconds", 15), 1),
    version: Math.max(pickNumber("version", "Version", pickNumber("schemaVersion", "SchemaVersion", 1)), 1),
    schemaVersion: Math.max(pickNumber("schemaVersion", "SchemaVersion", 1), 1)
  };
}
