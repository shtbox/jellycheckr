import { HttpApiClient } from "./api/client";
import { mountAysw } from "./integration/jellyfinHooks";
import { debug, warn } from "./logging/logger";
import type { PlayerAdapter } from "./player/playerAdapter";
import type { MediaItem } from "./player/playerAdapter";
import type { AyswModule, WebClientBootstrapContext } from "./integration/jellyfinHooks";

const HOST_RETRY_MS = 250;
const UNRESOLVED_SESSION_RETRY_MS = 2_000;
const REGISTER_FAILURE_INITIAL_RETRY_MS = 5_000;
const REGISTER_FAILURE_BACKOFF_RETRY_MS = 15_000;
const HARD_MOUNT_RETRY_MS = 10_000;
const HEARTBEAT_INTERVAL_MS = 30_000;

type AutoMountBootstrapState = "waiting_for_host" | "resolving_session" | "registered" | "mounted";

interface HostContext {
  accessToken: string;
  deviceId: string;
}

interface ApiClientHost {
  accessToken?: () => string;
  deviceId?: () => string;
}

let activeVideo: HTMLVideoElement | null = null;
let activeModule: AyswModule | null = null;
let activeSessionId: string | null = null;
let activeDeviceId: string | null = null;
let mountInFlight = false;
let scheduledCheck = false;
let pendingRetryTimerId: number | null = null;
let heartbeatIntervalId: number | null = null;
let lastCheckReason = "startup";
let bootstrapState: AutoMountBootstrapState = "waiting_for_host";
let nextRegisterRetryMs = REGISTER_FAILURE_INITIAL_RETRY_MS;

const registrationApi = new HttpApiClient();

declare global {
  interface Window {
    ApiClient?: ApiClientHost;
    JellycheckrAysw?: {
      mount(player: PlayerAdapter): Promise<{ dispose(): void }>;
      remountNow?(): void;
      getAutoMountStatus?(): {
        active: boolean;
        activeVideoConnected: boolean;
        hasVideoElement: boolean;
        mountInFlight: boolean;
        lastCheckReason: string;
        bootstrapState: AutoMountBootstrapState;
        sessionRegistered: boolean;
        sessionId: string | null;
      };
    };
  }
}

window.JellycheckrAysw = {
  mount: mountAysw,
  remountNow(): void {
    scheduleAutoMountCheck("manual");
  },
  getAutoMountStatus() {
    const currentVideo = findBestVideoElement();
    return {
      active: !!activeModule,
      activeVideoConnected: !!activeVideo?.isConnected,
      hasVideoElement: !!currentVideo,
      mountInFlight,
      lastCheckReason,
      bootstrapState,
      sessionRegistered: !!activeSessionId,
      sessionId: activeSessionId
    };
  }
};

debug("Registered window.JellycheckrAysw API");

startAutoMountMonitor();

function startAutoMountMonitor(): void {
  debug("Starting automatic mount monitor");
  scheduleAutoMountCheck("startup");

  const trigger = (reason: string): void => scheduleAutoMountCheck(reason);

  const observer = new MutationObserver(() => trigger("dom-mutation"));
  observer.observe(document.documentElement, { childList: true, subtree: true });

  window.addEventListener("pageshow", () => trigger("pageshow"));
  window.addEventListener("pagehide", () => {
    void disposeActiveModule("pagehide");
  });
  window.addEventListener("beforeunload", () => {
    void disposeActiveModule("beforeunload");
  });
  window.addEventListener("hashchange", () => trigger("hashchange"));
  window.addEventListener("popstate", () => trigger("popstate"));
  document.addEventListener("visibilitychange", () => {
    if (document.visibilityState === "visible") {
      trigger("visibilitychange");
    }
  });

  patchHistoryMethod("pushState", "pushstate");
  patchHistoryMethod("replaceState", "replacestate");

  window.setInterval(() => trigger("interval"), 5_000);
}

function patchHistoryMethod(methodName: "pushState" | "replaceState", reason: string): void {
  const historyObj = window.history as History & {
    __jellycheckrPatchedPushState?: boolean;
    __jellycheckrPatchedReplaceState?: boolean;
  };
  const flagName =
    methodName === "pushState"
      ? "__jellycheckrPatchedPushState"
      : "__jellycheckrPatchedReplaceState";

  if (historyObj[flagName]) {
    return;
  }

  const original = history[methodName];
  history[methodName] = function patchedHistoryMethod(this: History, ...args: any[]) {
    const result = original.apply(this, args as [any, string, (string | URL | null)?]);
    scheduleAutoMountCheck(reason);
    return result;
  };
  historyObj[flagName] = true;
}

function scheduleAutoMountCheck(reason: string, delayMs = 0): void {
  lastCheckReason = reason;

  if (delayMs <= 0) {
    clearPendingRetry();
    queueImmediateAutoMountCheck(reason);
    return;
  }

  if (pendingRetryTimerId !== null) {
    return;
  }

  pendingRetryTimerId = window.setTimeout(() => {
    pendingRetryTimerId = null;
    queueImmediateAutoMountCheck(reason);
  }, delayMs);
}

function queueImmediateAutoMountCheck(reason: string): void {
  if (scheduledCheck) {
    return;
  }

  scheduledCheck = true;
  window.setTimeout(() => {
    scheduledCheck = false;
    void ensureAutoMounted(reason);
  }, 0);
}

function clearPendingRetry(): void {
  if (pendingRetryTimerId === null) {
    return;
  }

  window.clearTimeout(pendingRetryTimerId);
  pendingRetryTimerId = null;
}

async function ensureAutoMounted(reason: string): Promise<void> {
  const video = findBestVideoElement();

  if (!video) {
    if (activeModule || activeSessionId || activeDeviceId) {
      debug("Disposing AYSW module because no compatible video element remains", { reason });
      await disposeActiveModule("video-missing");
    }

    bootstrapState = "waiting_for_host";
    resetRegisterBackoff();
    debug("No video element found during auto-mount check", { reason });
    return;
  }

  if (activeModule && activeVideo === video) {
    return;
  }

  if (mountInFlight) {
    debug("Skipping auto-mount check because a mount is already in-flight", { reason });
    return;
  }

  if (activeModule && activeVideo && activeVideo !== video) {
    debug("Disposing AYSW module before mounting on a new video element");
    await disposeActiveModule("video-changed");
  }

  const hostContext = resolveHostContext();
  if (!hostContext) {
    bootstrapState = "waiting_for_host";
    resetRegisterBackoff();
    debug("Host prerequisites are not ready yet", { reason });
    scheduleAutoMountCheck("waiting_for_host", HOST_RETRY_MS);
    return;
  }

  mountInFlight = true;
  try {
    bootstrapState = "resolving_session";
    const registration = await registerCurrentSession(hostContext, reason);
    if (!registration) {
      return;
    }

    activeSessionId = registration.sessionId;
    activeDeviceId = hostContext.deviceId;
    bootstrapState = "registered";

    debug("Attempting automatic mount", { reason, sessionId: activeSessionId, deviceId: activeDeviceId });
    const adapter = createDomPlayerAdapter(video);
    const module = await mountAysw(adapter, registration.bootstrap);
    activeVideo = video;
    activeModule = module;
    bootstrapState = "mounted";
    startHeartbeatLoop();
    debug("Automatic mount completed", { reason, sessionId: activeSessionId });
  } catch (err) {
    await unregisterActiveSession("mount_failed");
    bootstrapState = "waiting_for_host";
    warn("Automatic mount failed after session registration; continuing without Jellycheckr UI", err);
    scheduleAutoMountCheck("mount_failed", HARD_MOUNT_RETRY_MS);
  } finally {
    mountInFlight = false;
  }
}

async function registerCurrentSession(
  hostContext: HostContext,
  reason: string
): Promise<{ sessionId: string; bootstrap: WebClientBootstrapContext } | null> {
  try {
    const response = await registrationApi.registerWebClient({
      deviceId: hostContext.deviceId
    });

    if (!response.registered || !response.sessionId || !response.config) {
      bootstrapState = "waiting_for_host";
      resetRegisterBackoff();
      debug("Web client registration did not resolve a playable session yet", {
        reason,
        deviceId: hostContext.deviceId,
        response
      });
      scheduleAutoMountCheck("session_unresolved", UNRESOLVED_SESSION_RETRY_MS);
      return null;
    }

    resetRegisterBackoff();
    debug("Web client registration completed", {
      reason,
      sessionId: response.sessionId,
      deviceId: hostContext.deviceId,
      leaseExpiresUtc: response.leaseExpiresUtc ?? null
    });

    return {
      sessionId: response.sessionId,
      bootstrap: {
        config: response.config,
        deviceId: hostContext.deviceId
      }
    };
  } catch (err) {
    const retryMs = consumeRegisterRetryDelay();
    bootstrapState = "waiting_for_host";
    warn("Web client registration failed; retrying", err);
    scheduleAutoMountCheck("register_failed", retryMs);
    return null;
  }
}

function resolveHostContext(): HostContext | null {
  const apiClient = window.ApiClient;
  const accessToken = apiClient?.accessToken?.();
  const deviceId = apiClient?.deviceId?.();

  if (!accessToken || !deviceId) {
    return null;
  }

  return { accessToken, deviceId };
}

function resetRegisterBackoff(): void {
  nextRegisterRetryMs = REGISTER_FAILURE_INITIAL_RETRY_MS;
}

function consumeRegisterRetryDelay(): number {
  const delay = nextRegisterRetryMs;
  nextRegisterRetryMs = REGISTER_FAILURE_BACKOFF_RETRY_MS;
  return delay;
}

function startHeartbeatLoop(): void {
  stopHeartbeatLoop();
  heartbeatIntervalId = window.setInterval(() => {
    void sendHeartbeat();
  }, HEARTBEAT_INTERVAL_MS);
}

function stopHeartbeatLoop(): void {
  if (heartbeatIntervalId === null) {
    return;
  }

  window.clearInterval(heartbeatIntervalId);
  heartbeatIntervalId = null;
}

async function sendHeartbeat(): Promise<void> {
  if (!activeModule || !activeDeviceId) {
    return;
  }

  try {
    const response = await registrationApi.heartbeatWebClient({
      deviceId: activeDeviceId,
      sessionId: activeSessionId ?? undefined
    });

    if (!response.accepted) {
      debug("Web client heartbeat was not accepted; disposing current module", {
        sessionId: activeSessionId,
        deviceId: activeDeviceId,
        response
      });
      await disposeActiveModule("heartbeat_rejected");
      scheduleAutoMountCheck("heartbeat_rejected", UNRESOLVED_SESSION_RETRY_MS);
      return;
    }

    if (response.sessionId) {
      activeSessionId = response.sessionId;
    }
  } catch (err) {
    warn("Web client heartbeat failed; retaining the current registration until the lease expires", err);
  }
}

async function disposeActiveModule(reason = "dispose"): Promise<void> {
  stopHeartbeatLoop();

  try {
    activeModule?.dispose();
  } catch (err) {
    warn("Failed to dispose AYSW module", err);
  } finally {
    activeModule = null;
    activeVideo = null;
  }

  await unregisterActiveSession(reason);
  bootstrapState = "waiting_for_host";
}

async function unregisterActiveSession(reason: string): Promise<void> {
  const sessionId = activeSessionId;
  const deviceId = activeDeviceId;

  activeSessionId = null;
  activeDeviceId = null;

  if (!sessionId && !deviceId) {
    return;
  }

  try {
    await registrationApi.unregisterWebClient({
      sessionId: sessionId ?? undefined,
      deviceId: deviceId ?? undefined
    });
    debug("Unregistered web client session", { reason, sessionId, deviceId });
  } catch (err) {
    debug("Failed to unregister web client session (non-fatal)", { reason, sessionId, deviceId, err });
  }
}

function findBestVideoElement(): HTMLVideoElement | null {
  const videos = Array.from(document.querySelectorAll("video"))
    .filter((node): node is HTMLVideoElement => node instanceof HTMLVideoElement);

  if (videos.length === 0) {
    return null;
  }

  const visible = videos.find((video) => video.getClientRects().length > 0);
  return visible ?? videos[0] ?? null;
}

function createDomPlayerAdapter(video: HTMLVideoElement): PlayerAdapter {
  return {
    getSessionId(): string {
      return activeSessionId ?? getCurrentHostDeviceId() ?? `web-${window.location.host}`;
    },
    getDeviceId(): string | null {
      return activeDeviceId ?? getCurrentHostDeviceId();
    },
    getCurrentItem(): MediaItem | null {
      return null;
    },
    stopPlayback(): void {
      debug("Stopping playback via DOM adapter");
      video.pause();
      try {
        video.currentTime = 0;
      } catch {
        debug("Video seek reset not supported by current stream");
      }
    },
    exitPlaybackView(): void {
      debug("Attempting to exit playback view via DOM adapter");
      window.setTimeout(() => {
        tryExitPlaybackView();
      }, 150);
    },
    on(eventName: string, handler: (...args: unknown[]) => void): () => void {
      if (eventName === "seek") {
        debug("Binding DOM adapter event", { eventName, domEvent: "seeked" });
        const fn = (): void => handler();
        video.addEventListener("seeked", fn);
        return () => video.removeEventListener("seeked", fn);
      }

      if (eventName === "pause") {
        debug("Binding DOM adapter event", { eventName, domEvent: "pause" });
        const fn = (): void => handler();
        video.addEventListener("pause", fn);
        return () => video.removeEventListener("pause", fn);
      }

      if (eventName === "itemchange") {
        debug("Binding DOM adapter event", { eventName, domEvent: "loadedmetadata" });
        const fn = (): void => handler();
        video.addEventListener("loadedmetadata", fn);
        return () => video.removeEventListener("loadedmetadata", fn);
      }

      debug("Ignoring unsupported DOM adapter event subscription", { eventName });
      return () => {};
    }
  };
}

function getCurrentHostDeviceId(): string | null {
  return window.ApiClient?.deviceId?.() ?? null;
}

function tryExitPlaybackView(): void {
  try {
    const dashboard = (window as unknown as { Dashboard?: { back?: () => void } }).Dashboard;
    if (typeof dashboard?.back === "function") {
      dashboard.back();
      debug("Exited playback view using Dashboard.back()");
      return;
    }
  } catch (err) {
    debug("Dashboard.back() navigation failed", { err });
  }

  const backButtonSelectors = [
    "button.btnBack",
    ".btnBack",
    "button[aria-label='Back']",
    "button[title='Back']",
    "[data-action='back']",
    ".headerButton.headerButton-left"
  ];

  for (const selector of backButtonSelectors) {
    const target = document.querySelector(selector);
    if (!(target instanceof HTMLElement)) {
      continue;
    }

    target.click();
    debug("Exited playback view by clicking back button", { selector });
    return;
  }

  if (window.history.length > 1) {
    debug("Exited playback view using history.back()", { historyLength: window.history.length });
    window.history.back();
    return;
  }

  debug("No supported playback-exit navigation path found");
}
