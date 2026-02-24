import type { EffectiveConfigResponse } from "../../../../packages/contracts/ts/index";
import type { MediaItem } from "../player/playerAdapter";
import { shouldPrompt, type AyswState } from "../state/ayswStateMachine";

export type HudTransportStatus = "idle" | "pending" | "ok" | "error";

export type AyswDeveloperHudModuleState = "mounting" | "mounted" | "disposing";

export interface AyswDeveloperHudSnapshot {
  moduleState: AyswDeveloperHudModuleState;
  sessionId: string;
  mountedAtTs: number;
  config: EffectiveConfigResponse;
  currentItem: MediaItem | null;
  state: AyswState;
  modalVisible: boolean;
  lastInteraction: {
    eventType: string | null;
    atTs: number | null;
    itemId: string | null;
    sendStatus: HudTransportStatus;
    sendAtTs: number | null;
    sendError: string | null;
  };
  lastEvaluation: {
    trigger: string | null;
    atTs: number | null;
    promptEligible: boolean | null;
    decision: string | null;
    blockers: string[];
    note: string | null;
  };
  lastPrompt: {
    shownAtTs: number | null;
    shownSendStatus: HudTransportStatus;
    shownSendAtTs: number | null;
    shownSendError: string | null;
    closedAtTs: number | null;
    closeReason: string | null;
  };
  lastAck: {
    ackType: "continue" | "stop" | null;
    status: HudTransportStatus;
    atTs: number | null;
    error: string | null;
  };
  lastServerCall: {
    kind: string | null;
    status: HudTransportStatus;
    atTs: number | null;
    note: string | null;
    error: string | null;
  };
}

export interface AyswDeveloperHudController {
  render(snapshot: AyswDeveloperHudSnapshot): void;
  pushEvent(message: string): void;
  dispose(): void;
}

const MAX_EVENTS = 10;

export function createAyswDeveloperHud(): AyswDeveloperHudController {
  let root: HTMLDivElement | null = null;
  let pre: HTMLPreElement | null = null;
  let intervalId: number | null = null;
  let lastSnapshot: AyswDeveloperHudSnapshot | null = null;
  const events: string[] = [];

  const ensureRoot = (): void => {
    if (root && pre) {
      return;
    }

    injectStyles();

    root = document.createElement("div");
    root.className = "jellycheckr-devhud";
    root.setAttribute("aria-hidden", "true");

    pre = document.createElement("pre");
    pre.className = "jellycheckr-devhud-pre";
    root.appendChild(pre);

    document.body.appendChild(root);
  };

  const draw = (): void => {
    if (!lastSnapshot) {
      return;
    }
    ensureRoot();
    if (!pre) {
      return;
    }
    pre.textContent = formatHudText(lastSnapshot, events);
  };

  const ensureTicker = (): void => {
    if (intervalId !== null) {
      return;
    }
    intervalId = window.setInterval(draw, 1000);
  };

  return {
    render(snapshot: AyswDeveloperHudSnapshot): void {
      lastSnapshot = snapshot;
      draw();
      ensureTicker();
    },
    pushEvent(message: string): void {
      const ts = new Date().toLocaleTimeString();
      events.unshift(`${ts} ${message}`);
      if (events.length > MAX_EVENTS) {
        events.length = MAX_EVENTS;
      }
      draw();
    },
    dispose(): void {
      if (intervalId !== null) {
        window.clearInterval(intervalId);
        intervalId = null;
      }
      root?.remove();
      root = null;
      pre = null;
      lastSnapshot = null;
      events.length = 0;
    }
  };
}

function formatHudText(snapshot: AyswDeveloperHudSnapshot, events: string[]): string {
  const now = Date.now();
  const state = snapshot.state;
  const config = snapshot.config;
  const currentItem = snapshot.currentItem;

  const minutesWithoutInteraction = (now - state.lastInteractionTs) / 60000;
  const secondsSincePromptReset = (now - state.lastPromptResetTs) / 1000;
  const episodeThresholdReached = state.episodeTransitionsSinceAck >= config.episodeThreshold;
  const timeThresholdReached = minutesWithoutInteraction >= config.minutesThreshold;
  const developerThresholdReached =
    Boolean(config.developerMode) &&
    now - state.lastPromptResetTs >= config.developerPromptAfterSeconds * 1000;
  const promptEligibleNow = shouldPrompt(state, now, config);
  const popupNow = describePopupExpectation({
    modalVisible: snapshot.modalVisible,
    state,
    promptEligibleNow
  });
  const activeBlockers = getCurrentPromptBlockers({
    now,
    config,
    state,
    episodeThresholdReached,
    timeThresholdReached,
    developerThresholdReached
  });

  const lines: string[] = [];
  lines.push("JELLYCHECKR DEV HUD");
  lines.push(kv("module", `${snapshot.moduleState} | uptime ${formatAge(snapshot.mountedAtTs, now)}`));
  lines.push(kv("tracking", describeTrackingState(config.enabled, currentItem)));
  lines.push(kv("popup now", popupNow));
  lines.push(
    kv(
      "current item",
      currentItem
        ? `${safe(currentItem.type) || "Unknown"} ${clip(currentItem.id)} | series ${clip(currentItem.seriesId)}`
        : "(none)"
    )
  );
  lines.push(kv("session", clip(snapshot.sessionId, 24)));
  lines.push(
    kv(
      "config",
      `ep=${config.episodeThreshold} time=${config.minutesThreshold}m quiet=${config.interactionQuietSeconds}s prompt=${config.promptTimeoutSeconds}s cooldown=${config.cooldownMinutes}m dev=${config.developerMode ? `${config.developerPromptAfterSeconds}s` : "off"}`
    )
  );
  lines.push(kv("thresholds", `ep ${onOff(episodeThresholdReached)} | time ${onOff(timeThresholdReached)} | dev ${onOff(developerThresholdReached)}`));
  lines.push(
    kv(
      "progress",
      `ep ${state.episodeTransitionsSinceAck}/${config.episodeThreshold} | idle ${minutesWithoutInteraction.toFixed(1)}/${config.minutesThreshold}m | reset ${Math.floor(secondsSincePromptReset)}s/${config.developerPromptAfterSeconds}s`
    )
  );
  lines.push(
    kv(
      "state",
      `promptOpen=${onOff(state.promptOpen)} modalVisible=${onOff(snapshot.modalVisible)} nextEligible=${formatCountdownFromNow(state.nextEligiblePromptTs, now)}`
    )
  );
  lines.push(kv("deadline", state.promptDeadlineTs ? `${formatTime(state.promptDeadlineTs)} (${formatCountdown(state.promptDeadlineTs, now)})` : "-"));
  lines.push(
    kv(
      "last eval",
      formatLastEval(snapshot.lastEvaluation)
    )
  );
  lines.push(
    kv(
      "eval blockers",
      snapshot.lastEvaluation.blockers.length > 0 ? snapshot.lastEvaluation.blockers.join(", ") : "-"
    )
  );
  lines.push(
    kv(
      "active blockers",
      activeBlockers.length > 0 ? activeBlockers.join(", ") : "-"
    )
  );
  lines.push(kv("interaction", formatInteraction(snapshot.lastInteraction, now)));
  lines.push(kv("server", formatServerCall(snapshot.lastServerCall, now)));
  lines.push(kv("prompt", formatPrompt(snapshot.lastPrompt, now)));
  lines.push(kv("ack", formatAck(snapshot.lastAck, now)));

  if (events.length > 0) {
    lines.push("");
    lines.push("recent:");
    events.forEach((entry) => lines.push(`  ${entry}`));
  }

  return lines.join("\n");
}

function formatLastEval(
  lastEval: AyswDeveloperHudSnapshot["lastEvaluation"]
): string {
  if (!lastEval.atTs || !lastEval.trigger) {
    return "-";
  }

  const parts = [
    `${lastEval.trigger} ${formatAge(lastEval.atTs)}`,
    `eligible=${tri(lastEval.promptEligible)}`
  ];

  if (lastEval.decision) {
    parts.push(`decision=${lastEval.decision}`);
  }

  if (lastEval.note) {
    parts.push(`note=${clip(lastEval.note, 42)}`);
  }

  return parts.join(" | ");
}

function formatInteraction(
  interaction: AyswDeveloperHudSnapshot["lastInteraction"],
  now: number
): string {
  if (!interaction.atTs || !interaction.eventType) {
    return "-";
  }

  const parts = [
    `${interaction.eventType} ${formatAge(interaction.atTs, now)}`,
    `item=${clip(interaction.itemId)}`
  ];

  parts.push(`send=${interaction.sendStatus}`);

  if (interaction.sendAtTs) {
    parts.push(formatAge(interaction.sendAtTs, now));
  }

  if (interaction.sendError) {
    parts.push(`err=${clip(interaction.sendError, 34)}`);
  }

  return parts.join(" | ");
}

function formatServerCall(
  call: AyswDeveloperHudSnapshot["lastServerCall"],
  now: number
): string {
  if (!call.kind || !call.atTs) {
    return "-";
  }

  const parts = [`${call.kind}`, `status=${call.status}`, formatAge(call.atTs, now)];
  if (call.note) {
    parts.push(`note=${clip(call.note, 36)}`);
  }
  if (call.error) {
    parts.push(`err=${clip(call.error, 30)}`);
  }
  return parts.join(" | ");
}

function formatPrompt(
  prompt: AyswDeveloperHudSnapshot["lastPrompt"],
  now: number
): string {
  const parts: string[] = [];
  if (prompt.shownAtTs) {
    parts.push(`shown ${formatAge(prompt.shownAtTs, now)}`);
  }
  parts.push(`shownCall=${prompt.shownSendStatus}`);
  if (prompt.shownSendAtTs) {
    parts.push(formatAge(prompt.shownSendAtTs, now));
  }
  if (prompt.shownSendError) {
    parts.push(`err=${clip(prompt.shownSendError, 28)}`);
  }
  if (prompt.closedAtTs) {
    parts.push(`closed ${formatAge(prompt.closedAtTs, now)}`);
  }
  if (prompt.closeReason) {
    parts.push(`reason=${prompt.closeReason}`);
  }
  return parts.length > 0 ? parts.join(" | ") : "-";
}

function formatAck(
  ack: AyswDeveloperHudSnapshot["lastAck"],
  now: number
): string {
  if (!ack.atTs || !ack.ackType) {
    return "-";
  }

  const parts = [`${ack.ackType}`, `status=${ack.status}`, formatAge(ack.atTs, now)];
  if (ack.error) {
    parts.push(`err=${clip(ack.error, 30)}`);
  }
  return parts.join(" | ");
}

function describePopupExpectation(input: {
  modalVisible: boolean;
  state: AyswState;
  promptEligibleNow: boolean;
}): string {
  if (input.modalVisible) {
    return "visible";
  }

  if (input.state.promptOpen) {
    return "expected open, modal hidden (mismatch)";
  }

  if (input.promptEligibleNow) {
    return "eligible now (next evaluate should open)";
  }

  return "not eligible";
}

function getCurrentPromptBlockers(input: {
  now: number;
  config: EffectiveConfigResponse;
  state: AyswState;
  episodeThresholdReached: boolean;
  timeThresholdReached: boolean;
  developerThresholdReached: boolean;
}): string[] {
  const blockers: string[] = [];

  if (!input.config.enabled) {
    blockers.push("disabled");
  }
  if (input.state.promptOpen) {
    blockers.push("prompt_open");
  }
  if (input.state.nextEligiblePromptTs > input.now) {
    blockers.push(`cooldown ${formatCountdown(input.state.nextEligiblePromptTs, input.now)}`);
  }
  if (
    !input.episodeThresholdReached &&
    !input.timeThresholdReached &&
    !input.developerThresholdReached
  ) {
    blockers.push("thresholds_not_met");
  }

  return blockers;
}

function describeTrackingState(enabled: boolean, item: MediaItem | null): string {
  if (!enabled) {
    return "disabled (config)";
  }
  if (!item) {
    return "enabled, waiting for media item";
  }
  return "enabled, tracking current playback item";
}

function kv(label: string, value: string): string {
  return `${label.padEnd(14, " ")} ${value}`;
}

function onOff(value: boolean): string {
  return value ? "yes" : "no";
}

function tri(value: boolean | null): string {
  if (value === null) {
    return "?";
  }
  return value ? "yes" : "no";
}

function formatTime(ts: number): string {
  return new Date(ts).toLocaleTimeString();
}

function formatAge(ts: number, now = Date.now()): string {
  const deltaMs = Math.max(now - ts, 0);
  if (deltaMs < 1000) {
    return "0s ago";
  }
  const totalSeconds = Math.floor(deltaMs / 1000);
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  const hours = Math.floor(minutes / 60);

  if (hours > 0) {
    return `${hours}h${Math.floor((minutes % 60)).toString().padStart(2, "0")}m ago`;
  }
  if (minutes > 0) {
    return `${minutes}m${seconds.toString().padStart(2, "0")}s ago`;
  }
  return `${seconds}s ago`;
}

function formatCountdown(targetTs: number, now = Date.now()): string {
  const deltaMs = targetTs - now;
  if (deltaMs <= 0) {
    return "ready";
  }
  const totalSeconds = Math.ceil(deltaMs / 1000);
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  if (minutes > 0) {
    return `${minutes}m${seconds.toString().padStart(2, "0")}s`;
  }
  return `${seconds}s`;
}

function formatCountdownFromNow(targetTs: number, now = Date.now()): string {
  if (targetTs <= now) {
    return "ready";
  }
  return `${formatTime(targetTs)} (${formatCountdown(targetTs, now)})`;
}

function clip(value: string | null | undefined, max = 18): string {
  if (!value) {
    return "-";
  }
  if (value.length <= max) {
    return value;
  }
  return `${value.slice(0, Math.max(max - 3, 1))}...`;
}

function safe(value: string | undefined): string {
  return value ?? "";
}

function injectStyles(): void {
  if (document.getElementById("jellycheckr-devhud-style")) {
    return;
  }

  const style = document.createElement("style");
  style.id = "jellycheckr-devhud-style";
  style.textContent = `
    .jellycheckr-devhud {
      position: fixed;
      top: 10px;
      right: 10px;
      z-index: 99990;
      width: min(48vw, 560px);
      max-height: 45vh;
      overflow: auto;
      pointer-events: none;
      border-radius: 8px;
      border: 1px solid rgba(255,255,255,0.14);
      background:
        linear-gradient(180deg, rgba(8,12,16,0.82), rgba(6,8,11,0.92));
      box-shadow: 0 8px 28px rgba(0,0,0,0.45);
      backdrop-filter: blur(4px);
    }

    .jellycheckr-devhud-pre {
      margin: 0;
      padding: 8px 9px;
      color: rgba(208, 255, 230, 0.95);
      text-shadow: 0 1px 1px rgba(0,0,0,0.4);
      font: 10px/1.34 Consolas, "SFMono-Regular", Menlo, Monaco, "Liberation Mono", monospace;
      white-space: pre-wrap;
      word-break: break-word;
    }

    @media (max-width: 900px) {
      .jellycheckr-devhud {
        width: min(62vw, 560px);
        max-height: 50vh;
      }
    }

    @media (max-width: 640px) {
      .jellycheckr-devhud {
        top: 6px;
        right: 6px;
        width: min(92vw, 560px);
        max-height: 42vh;
      }

      .jellycheckr-devhud-pre {
        font-size: 9px;
        line-height: 1.28;
        padding: 6px 7px;
      }
    }
  `;

  document.head.appendChild(style);
}
