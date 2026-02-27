export type PluginConfig = {
  Enabled: boolean;
  EnableEpisodeCheck: boolean;
  EnableTimerCheck: boolean;
  EnableServerFallback: boolean;
  EpisodeThreshold: number;
  MinutesThreshold: number;
  InteractionQuietSeconds: number;
  PromptTimeoutSeconds: number;
  CooldownMinutes: number;
  ServerFallbackInactivityMinutes: number;
  ServerFallbackPauseBeforeStop: boolean;
  ServerFallbackPauseGraceSeconds: number;
  ServerFallbackSendMessageBeforePause: boolean;
  ServerFallbackClientMessage: string;
  ServerFallbackDryRun: boolean;
  MinimumLogLevel: number;
  DebugLogging: boolean;
  DeveloperMode: boolean;
  DeveloperPromptAfterSeconds: number;
  SchemaVersion: number;
};

export type StatusTone = 'neutral' | 'ok' | 'warn' | 'error';

declare global {
  interface Window {
    ApiClient?: {
      getPluginConfiguration?: (pluginId: string) => Promise<any>;
      updatePluginConfiguration?: (pluginId: string, payload: unknown) => Promise<any>;
      getUrl?: (name: string) => string;
      serverAddress?: () => string;
      accessToken?: () => string;
      ajax?: (options: any) => Promise<any>;
    };
    Dashboard?: {
      showLoadingMsg?: () => void;
      hideLoadingMsg?: () => void;
      alert?: (message: string) => void;
      processPluginConfigurationUpdateResult?: (result: unknown) => void;
    };
    JellycheckrConfigAdmin?: { bind: () => void };
    __jellycheckrConfigUiInitialized?: boolean;
  }
}
