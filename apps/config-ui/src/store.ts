import { computed, signal } from '@preact/signals';

import { PLUGIN_ID } from './constants';
import { defaults, clamp, normalize } from './configSchema';
import { getApiClient, getDashboard, getJson, putJson } from './host';
import { adminDebug, adminWarn, setAdminVerboseLogging } from './logging';
import type { PluginConfig, StatusTone } from './types';

const ADMIN_CONFIG_PATH = '/Plugins/jellycheckr/admin/config';

export const config = signal<PluginConfig>({ ...defaults });
export const loading = signal(false);
export const saving = signal(false);
export const dirty = signal(false);
export const statusText = signal('Loading configuration...');
export const statusTone = signal<StatusTone>('neutral');
export const snapshot = signal(JSON.stringify(defaults));

export const modeLabel = computed(() => {
  switch (config.value.EnforcementMode) {
    case 0: return 'None';
    case 2: return 'ServerFallback';
    default: return 'WebOnly';
  }
});

export const summary = computed(() => {
  const c = config.value;
  const fallbackThresholdSummary =
    c.ServerFallbackEpisodeThreshold > 0 && c.ServerFallbackMinutesThreshold > 0
      ? `${c.ServerFallbackEpisodeThreshold} eps ${c.ServerFallbackTriggerMode === 1 ? 'and' : 'or'} ${c.ServerFallbackMinutesThreshold} min`
      : c.ServerFallbackEpisodeThreshold > 0
        ? `${c.ServerFallbackEpisodeThreshold} eps`
        : c.ServerFallbackMinutesThreshold > 0
          ? `${c.ServerFallbackMinutesThreshold} min`
          : 'disabled';
  const parts = [
    `${c.EpisodeThreshold} eps or ${c.MinutesThreshold} min`,
    `quiet ${c.InteractionQuietSeconds}s`,
    `timeout ${c.PromptTimeoutSeconds}s`,
    `cooldown ${c.CooldownMinutes}m`
  ];

  if (c.DeveloperMode) {
    parts.push(`dev ${Math.max(1, c.DeveloperPromptAfterSeconds)}s`);
  }
  if (c.EnforcementMode === 2) {
    parts.push(`fallback ${fallbackThresholdSummary}`);
    parts.push(`inactive ${c.ServerFallbackInactivityMinutes}m`);
    parts.push(c.ServerFallbackPauseBeforeStop ? `pause ${c.ServerFallbackPauseGraceSeconds}s` : 'direct stop');
    if (c.ServerFallbackDryRun) {
      parts.push('dry-run');
    }
  }

  return parts.join(' | ');
});

function markSnapshot(value: PluginConfig): void {
  snapshot.value = JSON.stringify(value);
  dirty.value = false;
}

export function setStatus(text: string, tone: StatusTone = 'neutral'): void {
  statusText.value = text;
  statusTone.value = tone;
  adminDebug('Status changed', { text, tone });
}

export function updateField<K extends keyof PluginConfig>(key: K, value: PluginConfig[K]): void {
  const nextConfig = { ...config.value, [key]: value };
  config.value = nextConfig;
  dirty.value = JSON.stringify(nextConfig) !== snapshot.value;
  setAdminVerboseLogging(nextConfig.DeveloperMode || nextConfig.DebugLogging);
  adminDebug('Field updated', {
    key,
    value,
    dirty: dirty.value,
    config: nextConfig
  });
}

export async function loadConfig(): Promise<void> {
  const apiClient = getApiClient();
  const dashboard = getDashboard();
  adminDebug('loadConfig invoked');

  if (!apiClient && typeof fetch !== 'function') {
    setStatus('Jellyfin ApiClient/fetch is unavailable on this page.', 'error');
    adminWarn('ApiClient and fetch unavailable during loadConfig');
    return;
  }

  loading.value = true;
  setStatus('Loading configuration...', 'neutral');
  dashboard?.showLoadingMsg?.();

  try {
    let raw: any;
    try {
      raw = await getJson(ADMIN_CONFIG_PATH);
      adminDebug('Raw admin endpoint configuration loaded', raw);
    } catch (adminErr) {
      adminWarn('Admin config endpoint load failed; falling back to generic plugin config API', adminErr);
      if (!apiClient?.getPluginConfiguration) {
        throw adminErr;
      }
      raw = await apiClient.getPluginConfiguration(PLUGIN_ID);
      adminDebug('Raw plugin configuration loaded', raw);
    }
    const normalized = normalize(raw);
    setAdminVerboseLogging(normalized.DeveloperMode || normalized.DebugLogging);
    config.value = normalized;
    markSnapshot(normalized);
    adminDebug('Normalized plugin configuration applied', normalized);
    setStatus('Configuration loaded.', 'ok');
  } catch (err) {
    setStatus('Failed to load configuration.', 'error');
    adminWarn('Failed to load configuration', err);
    dashboard?.alert?.('Failed to load configuration.');
  } finally {
    loading.value = false;
    dashboard?.hideLoadingMsg?.();
  }
}

export async function saveConfig(ev?: Event): Promise<void> {
  ev?.preventDefault();

  const apiClient = getApiClient();
  const dashboard = getDashboard();

  if (!apiClient && typeof fetch !== 'function') {
    setStatus('Jellyfin ApiClient/fetch is unavailable on this page.', 'error');
    adminWarn('ApiClient and fetch unavailable during saveConfig');
    return;
  }

  saving.value = true;
  setStatus('Saving configuration...', 'warn');
  dashboard?.showLoadingMsg?.();

  try {
    const payload = { ...config.value, SchemaVersion: 2 };
    adminDebug('Saving plugin configuration payload', payload);
    let savedConfig: any = null;
    let genericResult: any = null;
    try {
      savedConfig = await putJson(ADMIN_CONFIG_PATH, payload);
      adminDebug('Admin config endpoint save result', savedConfig);
    } catch (adminErr) {
      adminWarn('Admin config endpoint save failed; falling back to generic plugin config API', adminErr);
      if (!apiClient?.updatePluginConfiguration) {
        throw adminErr;
      }
      genericResult = await apiClient.updatePluginConfiguration(PLUGIN_ID, payload);
      adminDebug('Plugin configuration save result', genericResult);
      // Read back from the server to confirm what actually persisted.
      try {
        savedConfig = await getJson(ADMIN_CONFIG_PATH);
      } catch (reloadErr) {
        adminWarn('Admin config endpoint read-back failed after generic save; using local payload snapshot', reloadErr);
      }
    }

    const normalizedSaved = normalize(savedConfig ?? payload);
    config.value = normalizedSaved;
    markSnapshot(normalizedSaved);
    setStatus('Configuration saved successfully.', 'ok');
    if (genericResult !== null) {
      dashboard?.processPluginConfigurationUpdateResult?.(genericResult);
    }
  } catch (err) {
    dirty.value = true;
    setStatus('Failed to save configuration.', 'error');
    adminWarn('Failed to save configuration', err);
    dashboard?.hideLoadingMsg?.();
    dashboard?.alert?.('Failed to save configuration.');
  } finally {
    saving.value = false;
  }
}

export function numberHandler<K extends keyof PluginConfig>(key: K, min: number, max: number) {
  return (ev: any) => {
    const raw = parseInt(ev.currentTarget.value, 10);
    updateField(key, clamp(raw, min, max) as PluginConfig[K]);
  };
}
