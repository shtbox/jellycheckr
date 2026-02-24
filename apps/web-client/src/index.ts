import { mountAysw } from "./integration/jellyfinHooks";
import { debug, warn } from "./logging/logger";
import type { PlayerAdapter } from "./player/playerAdapter";
import type { MediaItem } from "./player/playerAdapter";
import type { AyswModule } from "./integration/jellyfinHooks";

const AUTO_MOUNT_RETRY_MS = 10_000;

let activeVideo: HTMLVideoElement | null = null;
let activeModule: AyswModule | null = null;
let mountInFlight = false;
let scheduledCheck = false;
let lastCheckReason = "startup";
const failedMountAttempts = new WeakMap<HTMLVideoElement, number>();

declare global {
  interface Window {
    JellycheckrAysw?: {
      mount(player: PlayerAdapter): Promise<{ dispose(): void }>;
      remountNow?(): void;
      getAutoMountStatus?(): {
        active: boolean;
        activeVideoConnected: boolean;
        hasVideoElement: boolean;
        mountInFlight: boolean;
        lastCheckReason: string;
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
      lastCheckReason
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

function scheduleAutoMountCheck(reason: string): void {
  lastCheckReason = reason;
  if (scheduledCheck) {
    return;
  }

  scheduledCheck = true;
  window.setTimeout(() => {
    scheduledCheck = false;
    void ensureAutoMounted(reason);
  }, 0);
}

async function ensureAutoMounted(reason: string): Promise<void> {
  const video = findBestVideoElement();

  if (!video) {
    if (activeVideo && !activeVideo.isConnected) {
      debug("Disposing AYSW module because active video was removed");
      disposeActiveModule();
    }
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

  const lastFailedAt = failedMountAttempts.get(video) ?? 0;
  if (lastFailedAt && Date.now() - lastFailedAt < AUTO_MOUNT_RETRY_MS) {
    return;
  }

  if (activeModule && activeVideo && activeVideo !== video) {
    debug("Disposing AYSW module before mounting on a new video element");
    disposeActiveModule();
  }

  debug("Attempting automatic mount", { reason });
  mountInFlight = true;
  try {
    const adapter = createDomPlayerAdapter(video);
    const module = await mountAysw(adapter);
    activeVideo = video;
    activeModule = module;
    failedMountAttempts.delete(video);
    debug("Automatic mount completed", { reason });
  } catch (err) {
    failedMountAttempts.set(video, Date.now());
    warn("Automatic mount failed; continuing without Jellycheckr UI", err);
  } finally {
    mountInFlight = false;
  }
}

function disposeActiveModule(): void {
  try {
    activeModule?.dispose();
  } catch (err) {
    warn("Failed to dispose AYSW module", err);
  } finally {
    activeModule = null;
    activeVideo = null;
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
      const apiClient = (window as unknown as { ApiClient?: { deviceId?: () => string } }).ApiClient;
      return apiClient?.deviceId?.() ?? `web-${window.location.host}`;
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
        // Some streams do not support seek reset.
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
