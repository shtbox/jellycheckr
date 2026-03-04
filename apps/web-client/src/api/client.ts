import type {
  AckRequest,
  AckResponse,
  EffectiveConfigResponse,
  WebClientHeartbeatRequest,
  WebClientHeartbeatResponse,
  WebClientRegisterRequest,
  WebClientRegisterResponse,
  WebClientUnregisterRequest,
  PromptShownRequest,
  InteractionRequest,
  InteractionResponse
} from "../../../../packages/contracts/ts/index";
import { debug } from "../logging/logger";

export interface ApiClient {
  getEffectiveConfig(): Promise<EffectiveConfigResponse>;
  registerWebClient(body: WebClientRegisterRequest): Promise<WebClientRegisterResponse>;
  heartbeatWebClient(body: WebClientHeartbeatRequest): Promise<WebClientHeartbeatResponse>;
  unregisterWebClient(body: WebClientUnregisterRequest): Promise<void>;
  sendAck(sessionId: string, body: AckRequest): Promise<AckResponse>;
  sendInteraction(sessionId: string, body: InteractionRequest): Promise<InteractionResponse>;
  sendPromptShown(sessionId: string, body: PromptShownRequest): Promise<void>;
}

export class HttpApiClient implements ApiClient {
  private readonly basePath = "/Plugins/Aysw";

  async getEffectiveConfig(): Promise<EffectiveConfigResponse> {
    return this.request<EffectiveConfigResponse>(`${this.basePath}/config`, { method: "GET" });
  }

  async registerWebClient(body: WebClientRegisterRequest): Promise<WebClientRegisterResponse> {
    return this.request<WebClientRegisterResponse>(`${this.basePath}/web-client/register`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body)
    });
  }

  async heartbeatWebClient(body: WebClientHeartbeatRequest): Promise<WebClientHeartbeatResponse> {
    return this.request<WebClientHeartbeatResponse>(`${this.basePath}/web-client/heartbeat`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body)
    });
  }

  async unregisterWebClient(body: WebClientUnregisterRequest): Promise<void> {
    await this.request<void>(`${this.basePath}/web-client/unregister`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
      keepalive: true
    });
  }

  async sendAck(sessionId: string, body: AckRequest): Promise<AckResponse> {
    return this.request<AckResponse>(`${this.basePath}/sessions/${encodeURIComponent(sessionId)}/ack`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body)
    });
  }

  async sendInteraction(sessionId: string, body: InteractionRequest): Promise<InteractionResponse> {
    return this.request<InteractionResponse>(
      `${this.basePath}/sessions/${encodeURIComponent(sessionId)}/interaction`,
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body)
      }
    );
  }

  async sendPromptShown(sessionId: string, body: PromptShownRequest): Promise<void> {
    await this.request<void>(`${this.basePath}/sessions/${encodeURIComponent(sessionId)}/prompt-shown`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body)
    });
  }

  private async request<T>(url: string, init: RequestInit): Promise<T> {
    const method = init.method ?? "GET";
    debug("HTTP request", { method, url });

    const headers = new Headers(init.headers);
    const authHeaders = buildAuthHeaders();
    Object.entries(authHeaders).forEach(([key, value]) => headers.set(key, value));

    let response: Response;
    try {
      response = await fetch(url, {
        ...init,
        headers
      });
    } catch (err) {
      debug("HTTP request failed before response", { method, url, err });
      throw err;
    }

    debug("HTTP response", { method, url, status: response.status });
    if (!response.ok) {
      throw new Error(`[Jellycheckr] Request failed: ${response.status} ${url}`);
    }

    if (response.status === 204) {
      return undefined as T;
    }

    const contentType = response.headers.get("Content-Type") ?? "";
    if (!contentType.includes("application/json")) {
      debug("Skipping JSON parse for non-JSON response", { method, url, contentType });
      return undefined as T;
    }

    return (await response.json()) as T;
  }
}

function buildAuthHeaders(): Record<string, string> {
  const apiClient = (window as unknown as { ApiClient?: { accessToken?: () => string } }).ApiClient;
  const token = apiClient?.accessToken?.();
  if (token) {
    return { "X-Emby-Token": token };
  }

  return {};
}
