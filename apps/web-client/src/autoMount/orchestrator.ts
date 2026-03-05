import type {
  WebClientHeartbeatRequest,
  WebClientHeartbeatResponse,
  WebClientRegisterRequest,
  WebClientRegisterResponse,
  WebClientUnregisterRequest
} from "../../../../packages/contracts/ts/index";
import type { AyswModule, WebClientBootstrapContext } from "../integration/jellyfinHooks";
import type { PlayerAdapter } from "../player/playerAdapter";

const HOST_RETRY_MS = 250;
const UNRESOLVED_SESSION_RETRY_MS = 2_000;
const REGISTER_FAILURE_INITIAL_RETRY_MS = 5_000;
const REGISTER_FAILURE_BACKOFF_RETRY_MS = 15_000;
const HARD_MOUNT_RETRY_MS = 10_000;
const HEARTBEAT_INTERVAL_MS = 30_000;

export type AutoMountBootstrapState = "waiting_for_host" | "resolving_session" | "registered" | "mounted";

export interface HostContext {
  accessToken: string;
  deviceId: string;
}

export interface AutoMountStatus {
  active: boolean;
  activeVideoConnected: boolean;
  hasVideoElement: boolean;
  mountInFlight: boolean;
  lastCheckReason: string;
  bootstrapState: AutoMountBootstrapState;
  sessionRegistered: boolean;
  sessionId: string | null;
}

export interface AutoMountRegistrationApi {
  registerWebClient(body: WebClientRegisterRequest): Promise<WebClientRegisterResponse>;
  heartbeatWebClient(body: WebClientHeartbeatRequest): Promise<WebClientHeartbeatResponse>;
  unregisterWebClient(body: WebClientUnregisterRequest): Promise<void>;
}

export interface AutoMountScheduler {
  setTimeout(handler: () => void, delayMs: number): unknown;
  clearTimeout(timerId: unknown): void;
  setInterval(handler: () => void, delayMs: number): unknown;
  clearInterval(timerId: unknown): void;
}

export interface PlayerAdapterContext {
  getSessionId(): string | null;
  getDeviceId(): string | null;
}

export interface AutoMountOrchestratorDependencies<TVideo> {
  registrationApi: AutoMountRegistrationApi;
  mountAysw(player: PlayerAdapter, bootstrap: WebClientBootstrapContext): Promise<AyswModule>;
  resolveHostContext(): HostContext | null;
  findBestVideoElement(): TVideo | null;
  isVideoConnected(video: TVideo | null): boolean;
  createPlayerAdapter(video: TVideo, context: PlayerAdapterContext): PlayerAdapter;
  debug(...args: unknown[]): void;
  warn(...args: unknown[]): void;
  scheduler?: AutoMountScheduler;
}

export interface AutoMountOrchestrator {
  scheduleCheck(reason: string, delayMs?: number): void;
  dispose(reason?: string, unregisterSession?: boolean): Promise<void>;
  getStatus(): AutoMountStatus;
}

export function createAutoMountOrchestrator<TVideo>(
  deps: AutoMountOrchestratorDependencies<TVideo>
): AutoMountOrchestrator {
  const scheduler = deps.scheduler ?? createDefaultScheduler();

  let activeVideo: TVideo | null = null;
  let activeModule: AyswModule | null = null;
  let activeSessionId: string | null = null;
  let activeDeviceId: string | null = null;
  let activeBootstrap: WebClientBootstrapContext | null = null;
  let mountInFlight = false;
  let scheduledCheck = false;
  let pendingRetryTimerId: unknown | null = null;
  let heartbeatIntervalId: unknown | null = null;
  let lastCheckReason = "startup";
  let bootstrapState: AutoMountBootstrapState = "waiting_for_host";
  let nextRegisterRetryMs = REGISTER_FAILURE_INITIAL_RETRY_MS;

  function scheduleCheck(reason: string, delayMs = 0): void {
    lastCheckReason = reason;

    if (delayMs <= 0) {
      clearPendingRetry();
      queueImmediateCheck(reason);
      return;
    }

    if (pendingRetryTimerId !== null) {
      return;
    }

    pendingRetryTimerId = scheduler.setTimeout(() => {
      pendingRetryTimerId = null;
      queueImmediateCheck(reason);
    }, delayMs);
  }

  function queueImmediateCheck(reason: string): void {
    if (scheduledCheck) {
      return;
    }

    scheduledCheck = true;
    scheduler.setTimeout(() => {
      scheduledCheck = false;
      void ensureAutoMounted(reason);
    }, 0);
  }

  function clearPendingRetry(): void {
    if (pendingRetryTimerId === null) {
      return;
    }

    scheduler.clearTimeout(pendingRetryTimerId);
    pendingRetryTimerId = null;
  }

  async function ensureAutoMounted(reason: string): Promise<void> {
    const video = deps.findBestVideoElement();
    if (!video) {
      if (activeModule) {
        deps.debug("Disposing AYSW module because no compatible video element remains", { reason });
        await disposeActiveModule("video-missing", false);
      }

      bootstrapState = "waiting_for_host";
      deps.debug("No video element found during auto-mount check", { reason });
      return;
    }

    if (activeModule && hasStableActiveVideo()) {
      if (activeVideo !== video) {
        deps.debug("Keeping current mounted video binding despite a different candidate video", {
          reason,
          currentVideoConnected: deps.isVideoConnected(activeVideo)
        });
      }

      return;
    }

    if (mountInFlight) {
      deps.debug("Skipping auto-mount check because a mount is already in-flight", { reason });
      return;
    }

    if (activeModule && activeVideo && activeVideo !== video) {
      deps.debug("Disposing AYSW module before mounting on a new video element");
      await disposeActiveModule("video-changed", false);
    }

    const hostContext = deps.resolveHostContext();
    if (!hostContext) {
      bootstrapState = "waiting_for_host";
      resetRegisterBackoff();
      deps.debug("Host prerequisites are not ready yet", { reason });
      scheduleCheck("waiting_for_host", HOST_RETRY_MS);
      return;
    }

    mountInFlight = true;
    try {
      let registration: { sessionId: string; bootstrap: WebClientBootstrapContext } | null = null;
      if (hasReusableRegistrationContext(hostContext)) {
        bootstrapState = "registered";
        registration = {
          sessionId: activeSessionId!,
          bootstrap: activeBootstrap!
        };
      } else {
        bootstrapState = "resolving_session";
        registration = await registerCurrentSession(hostContext, reason);
        if (!registration) {
          return;
        }

        activeSessionId = registration.sessionId;
        activeDeviceId = hostContext.deviceId;
        activeBootstrap = registration.bootstrap;
      }

      bootstrapState = "registered";
      deps.debug("Attempting automatic mount", {
        reason,
        sessionId: activeSessionId,
        deviceId: activeDeviceId
      });
      const adapter = deps.createPlayerAdapter(video, {
        getSessionId: () => activeSessionId,
        getDeviceId: () => activeDeviceId
      });
      const module = await deps.mountAysw(adapter, registration.bootstrap);
      activeVideo = video;
      activeModule = module;
      bootstrapState = "mounted";
      startHeartbeatLoop();
      deps.debug("Automatic mount completed", { reason, sessionId: activeSessionId });
    } catch (err) {
      activeModule = null;
      activeVideo = null;
      bootstrapState = hasActiveRegistrationContext() ? "registered" : "waiting_for_host";
      deps.warn("Automatic mount failed after session registration; continuing without Jellycheckr UI", err);
      scheduleCheck("mount_failed", HARD_MOUNT_RETRY_MS);
    } finally {
      mountInFlight = false;
    }
  }

  async function registerCurrentSession(
    hostContext: HostContext,
    reason: string
  ): Promise<{ sessionId: string; bootstrap: WebClientBootstrapContext } | null> {
    try {
      const response = await deps.registrationApi.registerWebClient({
        deviceId: hostContext.deviceId
      });

      if (!response.registered || !response.sessionId || !response.config) {
        bootstrapState = "waiting_for_host";
        resetRegisterBackoff();
        deps.debug("Web client registration did not resolve a playable session yet", {
          reason,
          deviceId: hostContext.deviceId,
          response
        });
        scheduleCheck("session_unresolved", UNRESOLVED_SESSION_RETRY_MS);
        return null;
      }

      resetRegisterBackoff();
      deps.debug("Web client registration completed", {
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
      deps.warn("Web client registration failed; retrying", err);
      scheduleCheck("register_failed", retryMs);
      return null;
    }
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
    heartbeatIntervalId = scheduler.setInterval(() => {
      void sendHeartbeat();
    }, HEARTBEAT_INTERVAL_MS);
  }

  function stopHeartbeatLoop(): void {
    if (heartbeatIntervalId === null) {
      return;
    }

    scheduler.clearInterval(heartbeatIntervalId);
    heartbeatIntervalId = null;
  }

  async function sendHeartbeat(): Promise<void> {
    if (!activeModule || !activeDeviceId) {
      return;
    }

    try {
      const response = await deps.registrationApi.heartbeatWebClient({
        deviceId: activeDeviceId,
        sessionId: activeSessionId ?? undefined
      });
      if (!response.accepted) {
        deps.debug("Web client heartbeat was not accepted; disposing current module", {
          sessionId: activeSessionId,
          deviceId: activeDeviceId,
          response
        });
        await disposeActiveModule("heartbeat_rejected");
        scheduleCheck("heartbeat_rejected", UNRESOLVED_SESSION_RETRY_MS);
        return;
      }

      if (response.sessionId) {
        activeSessionId = response.sessionId;
      }
    } catch (err) {
      deps.warn("Web client heartbeat failed; retaining the current registration until the lease expires", err);
    }
  }

  async function disposeActiveModule(reason = "dispose", unregisterSession = true): Promise<void> {
    stopHeartbeatLoop();

    try {
      activeModule?.dispose();
    } catch (err) {
      deps.warn("Failed to dispose AYSW module", err);
    } finally {
      activeModule = null;
      activeVideo = null;
    }

    if (unregisterSession) {
      await unregisterActiveSession(reason);
      bootstrapState = "waiting_for_host";
      return;
    }

    bootstrapState = hasActiveRegistrationContext() ? "registered" : "waiting_for_host";
  }

  async function unregisterActiveSession(reason: string): Promise<void> {
    const sessionId = activeSessionId;
    const deviceId = activeDeviceId;
    activeSessionId = null;
    activeDeviceId = null;
    activeBootstrap = null;

    if (!sessionId && !deviceId) {
      return;
    }

    try {
      await deps.registrationApi.unregisterWebClient({
        sessionId: sessionId ?? undefined,
        deviceId: deviceId ?? undefined
      });
      deps.debug("Unregistered web client session", { reason, sessionId, deviceId });
    } catch (err) {
      deps.debug("Failed to unregister web client session (non-fatal)", {
        reason,
        sessionId,
        deviceId,
        err
      });
    }
  }

  function hasStableActiveVideo(): boolean {
    return deps.isVideoConnected(activeVideo);
  }

  function hasActiveRegistrationContext(): boolean {
    return !!activeSessionId && !!activeDeviceId && !!activeBootstrap;
  }

  function hasReusableRegistrationContext(hostContext: HostContext): boolean {
    return hasActiveRegistrationContext() && activeDeviceId === hostContext.deviceId;
  }

  function getStatus(): AutoMountStatus {
    const currentVideo = deps.findBestVideoElement();
    return {
      active: !!activeModule,
      activeVideoConnected: deps.isVideoConnected(activeVideo),
      hasVideoElement: !!currentVideo,
      mountInFlight,
      lastCheckReason,
      bootstrapState,
      sessionRegistered: !!activeSessionId,
      sessionId: activeSessionId
    };
  }

  return {
    scheduleCheck,
    dispose(reason = "dispose", unregisterSession = true): Promise<void> {
      return disposeActiveModule(reason, unregisterSession);
    },
    getStatus
  };
}

function createDefaultScheduler(): AutoMountScheduler {
  return {
    setTimeout(handler, delayMs) {
      return globalThis.setTimeout(handler, delayMs);
    },
    clearTimeout(timerId) {
      globalThis.clearTimeout(timerId as ReturnType<typeof setTimeout>);
    },
    setInterval(handler, delayMs) {
      return globalThis.setInterval(handler, delayMs);
    },
    clearInterval(timerId) {
      globalThis.clearInterval(timerId as ReturnType<typeof setInterval>);
    }
  };
}
