import { HttpApiClient } from "./api/client";
import {
  createAutoMountOrchestrator,
  type AutoMountBootstrapState,
  type HostContext,
  type PlayerAdapterContext
} from "./autoMount/orchestrator";
import { mountAysw } from "./integration/jellyfinHooks";
import { debug, warn } from "./logging/logger";
import type { MediaItem, PlayerAdapter } from "./player/playerAdapter";

interface ApiClientHost {
  accessToken?: () => string;
  deviceId?: () => string;
}

const registrationApi = new HttpApiClient();

const autoMountOrchestrator = createAutoMountOrchestrator<HTMLVideoElement>({
  registrationApi,
  mountAysw,
  resolveHostContext,
  findBestVideoElement,
  isVideoConnected(video): boolean {
    return video instanceof HTMLVideoElement && video.isConnected;
  },
  createPlayerAdapter: createDomPlayerAdapter,
  debug,
  warn
});

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
    autoMountOrchestrator.scheduleCheck("manual");
  },
  getAutoMountStatus() {
    return autoMountOrchestrator.getStatus();
  }
};

debug("Registered window.JellycheckrAysw API");

startAutoMountMonitor();

function startAutoMountMonitor(): void {
  debug("Starting automatic mount monitor");
  autoMountOrchestrator.scheduleCheck("startup");

  const trigger = (reason: string): void => autoMountOrchestrator.scheduleCheck(reason);

  const observer = new MutationObserver(() => trigger("dom-mutation"));
  observer.observe(document.documentElement, { childList: true, subtree: true });

  window.addEventListener("pageshow", () => trigger("pageshow"));
  window.addEventListener("pagehide", () => {
    void autoMountOrchestrator.dispose("pagehide");
  });
  window.addEventListener("beforeunload", () => {
    void autoMountOrchestrator.dispose("beforeunload");
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
    autoMountOrchestrator.scheduleCheck(reason);
    return result;
  };
  historyObj[flagName] = true;
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

function findBestVideoElement(): HTMLVideoElement | null {
  const videos = Array.from(document.querySelectorAll("video")).filter(
    (node): node is HTMLVideoElement => node instanceof HTMLVideoElement
  );

  if (videos.length === 0) {
    return null;
  }

  const visible = videos.find((video) => video.getClientRects().length > 0);
  return visible ?? videos[0] ?? null;
}

function createDomPlayerAdapter(video: HTMLVideoElement, context: PlayerAdapterContext): PlayerAdapter {
  return {
    getSessionId(): string {
      return context.getSessionId() ?? getCurrentHostDeviceId() ?? `web-${window.location.host}`;
    },
    getDeviceId(): string | null {
      return context.getDeviceId() ?? getCurrentHostDeviceId();
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
