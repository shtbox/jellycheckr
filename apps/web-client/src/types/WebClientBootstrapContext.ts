import type { EffectiveConfigResponse } from "../../../../packages/contracts/ts";


export interface WebClientBootstrapContext {
  config: EffectiveConfigResponse;
  deviceId?: string | null;
}
