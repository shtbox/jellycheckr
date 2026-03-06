import type { AckRequest, EffectiveConfigResponse } from "../../../../packages/contracts/ts/index";
import { HttpApiClient } from "../api/client";
import { debug, setDebugLogging, warn } from "../logging/logger";
import type { PlayerAdapter } from "../player/playerAdapter";
import {
  createInitialHudSnapshot,
  getPromptBlockers,
  summarizeError,
  withSafeDefaults
} from "./jellyfinHooks.helpers";
import {
  closePromptFromAck,
  createInitialState,
  openPrompt,
  registerInteraction,
  registerItemTransition,
  shouldPrompt
} from "../state/ayswStateMachine";
import { createModalController } from "../ui/modal";
import { createAyswDeveloperHud } from "../ui/developerHud";
import type { AyswDeveloperHudSnapshot } from "../types/AyswDeveloperHudSnapshot";
import type { AyswModule } from "../types/AyswModule";
import type { HudTransportStatus } from "../types/HudTransportStatus";
import type { WebClientBootstrapContext } from "../types/WebClientBootstrapContext";

export async function mountAysw(player: PlayerAdapter, bootstrap?: WebClientBootstrapContext): Promise<AyswModule> {
  const api = new HttpApiClient();
  let config: EffectiveConfigResponse;
  if (bootstrap?.config) {
    config = withSafeDefaults(bootstrap.config);
  } else {
    try {
      config = withSafeDefaults(await api.getEffectiveConfig());
    } catch (err) {
      warn("Failed to load effective config", err);
      throw err;
    }
  }
  setDebugLogging(config.debugLogging || config.developerMode);
  const resolveSessionId = (): string => player.getSessionId();
  const resolveDeviceId = (): string | undefined => bootstrap?.deviceId ?? player.getDeviceId?.() ?? undefined;
  const modal = createModalController({
    message: config.clientMessage
  });
  let state = createInitialState(Date.now());
  let lastMouseMoveTs = 0;
  let modalVisible = false;

  const devHud = config.developerMode ? createAyswDeveloperHud() : null;
  const hudSnapshot: AyswDeveloperHudSnapshot | null = devHud
    ? createInitialHudSnapshot({
        sessionId: resolveSessionId(),
        mountedAtTs: Date.now(),
        config,
        currentItem: player.getCurrentItem(),
        state,
        modalVisible
      })
    : null;

  const renderHud = (): void => {
    if (!devHud || !hudSnapshot) {
      return;
    }

    hudSnapshot.sessionId = resolveSessionId();
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
    sessionId: resolveSessionId(),
    config
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
      await api.sendInteraction(resolveSessionId(), {
        eventType,
        clientTimeUtc: new Date(now).toISOString(),
        itemId: currentItem?.id,
        clientType: "web",
        deviceId: resolveDeviceId()
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
    const episodeThresholdReached =
      config.enableEpisodeCheck &&
      state.episodeTransitionsSinceAck >= config.episodeThreshold;
    const timeThresholdReached =
      config.enableTimerCheck &&
      minutesWithoutInteraction >= config.minutesThreshold;
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
      sessionId: resolveSessionId(),
      nowIso: new Date(now).toISOString(),
      currentItem,
      promptEligible,
      state,
      thresholds: {
        episodeThresholdReached,
        timeThresholdReached,
        developerThresholdReached,
        minutesWithoutInteraction,
        enableEpisodeCheck: config.enableEpisodeCheck,
        enableTimerCheck: config.enableTimerCheck,
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
      await api.sendPromptShown(resolveSessionId(), {
        timeoutSeconds: config.promptTimeoutSeconds,
        itemId: currentItem?.id,
        clientType: "web",
        deviceId: resolveDeviceId()
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

    const handlePromptAction = async (ackType: AckRequest["ackType"], reason: string): Promise<void> => {
      debug("Prompt action selected", { action: ackType });
      if (ackType === "stop") {
        player.stopPlayback();
      }

      if (hudSnapshot) {
        hudSnapshot.lastAck.ackType = ackType;
        hudSnapshot.lastAck.status = "pending";
        hudSnapshot.lastAck.atTs = Date.now();
        hudSnapshot.lastAck.error = null;
      }
      setHudServerCall("ack", "pending", ackType);
      renderHud();
      pushHudEvent(`prompt action ${ackType}`);

      const ack: AckRequest = {
        ackType,
        clientTimeUtc: new Date().toISOString(),
        reason,
        itemId: player.getCurrentItem()?.id,
        clientType: "web",
        deviceId: resolveDeviceId()
      };

      try {
        await api.sendAck(resolveSessionId(), ack);
        if (hudSnapshot) {
          hudSnapshot.lastAck.status = "ok";
          hudSnapshot.lastAck.atTs = Date.now();
        }
        setHudServerCall("ack", "ok", ack.ackType);
        renderHud();
        pushHudEvent(`ack sent (${ackType})`);
        debug("Sent ack", { ackType: ack.ackType });
      } catch (err) {
        if (hudSnapshot) {
          hudSnapshot.lastAck.status = "error";
          hudSnapshot.lastAck.atTs = Date.now();
          hudSnapshot.lastAck.error = summarizeError(err);
        }
        setHudServerCall("ack", "error", ack.ackType, err);
        renderHud();
        pushHudEvent(`ack failed (${ackType})`);
        debug("Failed to send ack (non-fatal)", { ackType: ack.ackType, err });
      }

      state = closePromptFromAck(state, Date.now(), config.cooldownMinutes);
      modal.close();
      modalVisible = false;
      setHudPromptClosed(ackType);
      renderHud();
      pushHudEvent(`prompt closed (${ackType})`);

      if (ackType !== "stop") {
        return;
      }

      try {
        player.exitPlaybackView?.();
      } catch (err) {
        pushHudEvent("exit playback failed");
        debug("Failed to exit playback view after stop action (non-fatal)", { err });
      }
    };

    modal.show(
      async () => handlePromptAction("continue", "user_clicked_continue"),
      async () => handlePromptAction("stop", "timeout_or_user_stop"),
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

