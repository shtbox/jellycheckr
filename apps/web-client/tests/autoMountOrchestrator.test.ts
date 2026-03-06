import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type {
  EffectiveConfigResponse,
  WebClientHeartbeatResponse,
  WebClientRegisterResponse
} from "../../../packages/contracts/ts/index";
import { createAutoMountOrchestrator } from "../src/autoMount/orchestrator";

interface FakeVideo {
  connected: boolean;
}

const defaultConfig: EffectiveConfigResponse = {
  enabled: true,
  enableEpisodeCheck: true,
  enableTimerCheck: true,
  enableServerFallback: true,
  episodeThreshold: 3,
  minutesThreshold: 120,
  interactionQuietSeconds: 45,
  promptTimeoutSeconds: 60,
  cooldownMinutes: 30,
  serverFallbackInactivityMinutes: 30,
  serverFallbackPauseBeforeStop: true,
  serverFallbackPauseGraceSeconds: 45,
  serverFallbackSendMessageBeforePause: true,
  clientMessage: "Are you still watching?",
  serverFallbackDryRun: false,
  debugLogging: false,
  developerMode: false,
  developerPromptAfterSeconds: 15,
  version: 1,
  schemaVersion: 1
};

beforeEach(() => {
  vi.useFakeTimers();
});

afterEach(() => {
  vi.useRealTimers();
});

describe("auto-mount orchestrator", () => {
  it("register_once_after_success", async () => {
    const harness = createHarness();
    harness.orchestrator.scheduleCheck("startup");
    await advanceAndFlush(0);

    expect(harness.registerWebClient).toHaveBeenCalledTimes(1);
    expect(harness.mountAysw).toHaveBeenCalledTimes(1);

    for (let i = 0; i < 20; i += 1) {
      harness.orchestrator.scheduleCheck(`noise-${i}`);
    }
    await advanceAndFlush(0);

    expect(harness.registerWebClient).toHaveBeenCalledTimes(1);
    expect(harness.mountAysw).toHaveBeenCalledTimes(1);
  });

  it("session_unresolved_retry_policy", async () => {
    const harness = createHarness({
      registerResponseFactory: () => ({
        registered: false,
        reason: "session_unresolved"
      })
    });

    harness.orchestrator.scheduleCheck("startup");
    await advanceAndFlush(0);
    expect(harness.registerWebClient).toHaveBeenCalledTimes(1);

    await vi.advanceTimersByTimeAsync(1_999);
    expect(harness.registerWebClient).toHaveBeenCalledTimes(1);

    await advanceAndFlush(2);
    expect(harness.registerWebClient).toHaveBeenCalledTimes(2);

    await advanceAndFlush(2_001);
    expect(harness.registerWebClient).toHaveBeenCalledTimes(3);
  });

  it("register_failure_backoff", async () => {
    const harness = createHarness();
    harness.registerWebClient
      .mockRejectedValueOnce(new Error("register failure #1"))
      .mockRejectedValueOnce(new Error("register failure #2"))
      .mockResolvedValueOnce({
        registered: false,
        reason: "session_unresolved"
      });

    harness.orchestrator.scheduleCheck("startup");
    await advanceAndFlush(0);
    expect(harness.registerWebClient).toHaveBeenCalledTimes(1);

    await vi.advanceTimersByTimeAsync(4_999);
    expect(harness.registerWebClient).toHaveBeenCalledTimes(1);

    await advanceAndFlush(2);
    expect(harness.registerWebClient).toHaveBeenCalledTimes(2);

    await vi.advanceTimersByTimeAsync(14_999);
    expect(harness.registerWebClient).toHaveBeenCalledTimes(2);

    await advanceAndFlush(2);
    expect(harness.registerWebClient).toHaveBeenCalledTimes(3);
  });

  it("mount_failure_reuses_context", async () => {
    const harness = createHarness();
    harness.mountAysw
      .mockRejectedValueOnce(new Error("mount failure"))
      .mockResolvedValueOnce({ dispose: vi.fn() });

    harness.orchestrator.scheduleCheck("startup");
    await advanceAndFlush(0);

    expect(harness.registerWebClient).toHaveBeenCalledTimes(1);
    expect(harness.mountAysw).toHaveBeenCalledTimes(1);

    await advanceAndFlush(10_001);

    expect(harness.registerWebClient).toHaveBeenCalledTimes(1);
    expect(harness.mountAysw).toHaveBeenCalledTimes(2);
  });

  it("heartbeat_rejected_forces_recovery", async () => {
    const harness = createHarness();
    harness.heartbeatWebClient
      .mockResolvedValueOnce({
        accepted: false,
        reason: "session_unresolved"
      })
      .mockResolvedValue({
        accepted: true,
        sessionId: "s1"
      });

    harness.orchestrator.scheduleCheck("startup");
    await advanceAndFlush(0);
    expect(harness.registerWebClient).toHaveBeenCalledTimes(1);
    expect(harness.mountAysw).toHaveBeenCalledTimes(1);

    await vi.advanceTimersByTimeAsync(30_000);
    expect(harness.heartbeatWebClient).toHaveBeenCalledTimes(1);
    expect(harness.unregisterWebClient).toHaveBeenCalledTimes(1);

    await advanceAndFlush(2_001);
    expect(harness.registerWebClient).toHaveBeenCalledTimes(2);
    expect(harness.mountAysw).toHaveBeenCalledTimes(2);
  });

  it("dispose_paths", async () => {
    const harness = createHarness();
    harness.orchestrator.scheduleCheck("startup");
    await advanceAndFlush(0);

    expect(harness.moduleDispose).toHaveBeenCalledTimes(0);
    await harness.orchestrator.dispose("local_cleanup", false);
    expect(harness.moduleDispose).toHaveBeenCalledTimes(1);
    expect(harness.unregisterWebClient).toHaveBeenCalledTimes(0);

    await harness.orchestrator.dispose("full_cleanup", true);
    expect(harness.unregisterWebClient).toHaveBeenCalledTimes(1);
  });

  it("disposes_module_when_video_disappears", async () => {
    const harness = createHarness();
    harness.orchestrator.scheduleCheck("startup");
    await advanceAndFlush(0);

    expect(harness.mountAysw).toHaveBeenCalledTimes(1);
    expect(harness.moduleDispose).toHaveBeenCalledTimes(0);

    harness.setVideo(null);
    harness.orchestrator.scheduleCheck("dom-mutation");
    await advanceAndFlush(0);

    expect(harness.moduleDispose).toHaveBeenCalledTimes(1);
    expect(harness.unregisterWebClient).toHaveBeenCalledTimes(0);
    expect(harness.orchestrator.getStatus().active).toBe(false);
  });
});

interface HarnessOptions {
  registerResponseFactory?: () => WebClientRegisterResponse;
  heartbeatResponseFactory?: () => WebClientHeartbeatResponse;
}

function createHarness(options: HarnessOptions = {}) {
  let activeVideo: FakeVideo | null = { connected: true };
  const moduleDispose = vi.fn();
  const registerWebClient = vi.fn(async () => {
    const factory = options.registerResponseFactory;
    if (factory) {
      return factory();
    }

    return {
      registered: true,
      sessionId: "s1",
      config: defaultConfig
    } satisfies WebClientRegisterResponse;
  });
  const heartbeatWebClient = vi.fn(async () => {
    const factory = options.heartbeatResponseFactory;
    if (factory) {
      return factory();
    }

    return {
      accepted: true,
      sessionId: "s1"
    } satisfies WebClientHeartbeatResponse;
  });
  const unregisterWebClient = vi.fn(async () => {});
  const mountAysw = vi.fn(async () => ({ dispose: moduleDispose }));

  const orchestrator = createAutoMountOrchestrator<FakeVideo>({
    registrationApi: {
      registerWebClient,
      heartbeatWebClient,
      unregisterWebClient
    },
    mountAysw,
    resolveHostContext() {
      return {
        accessToken: "token",
        deviceId: "device-1"
      };
    },
    findBestVideoElement() {
      return activeVideo;
    },
    isVideoConnected(video) {
      return !!video?.connected;
    },
    createPlayerAdapter() {
      return {
        getSessionId() {
          return "player-session";
        },
        getDeviceId() {
          return "device-1";
        },
        getCurrentItem() {
          return null;
        },
        stopPlayback() {},
        on() {
          return () => {};
        }
      };
    },
    debug() {},
    warn() {}
  });

  return {
    orchestrator,
    registerWebClient,
    heartbeatWebClient,
    unregisterWebClient,
    mountAysw,
    moduleDispose,
    setVideo(video: FakeVideo | null) {
      activeVideo = video;
    }
  };
}

async function advanceAndFlush(ms: number): Promise<void> {
  await vi.advanceTimersByTimeAsync(ms);
  await vi.advanceTimersByTimeAsync(0);
}
