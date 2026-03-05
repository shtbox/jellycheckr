import type { EffectiveConfigResponse } from "../../../../packages/contracts/ts";
import type { MediaItem } from "../player/playerAdapter";
import type { AyswState } from "../state/ayswStateMachine";
import type { AyswDeveloperHudModuleState } from "./AyswDeveloperHudModuleState";
import type { HudTransportStatus } from "./HudTransportStatus";


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
