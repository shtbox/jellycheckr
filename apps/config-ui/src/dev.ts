import { defaults, normalize } from './configSchema';
import './tailwind.css';
import type { PluginConfig } from './types';

const DEV_STORAGE_KEY = 'jellycheckr.config-ui.dev.config';
const ADMIN_CONFIG_PATHS = new Set([
  '/plugins/jellycheckr/admin/config',
  '/plugins/aysw/admin/config'
]);

function clone<T>(value: T): T {
  return JSON.parse(JSON.stringify(value)) as T;
}

function toPath(url: string): string {
  try {
    return new URL(url, window.location.origin).pathname.toLowerCase();
  } catch {
    return String(url).toLowerCase();
  }
}

function isAdminConfigPath(url: string): boolean {
  return ADMIN_CONFIG_PATHS.has(toPath(url));
}

function readStoredConfig(): PluginConfig {
  const raw = window.localStorage.getItem(DEV_STORAGE_KEY);
  if (!raw) {
    return clone(defaults);
  }

  try {
    return normalize(JSON.parse(raw));
  } catch {
    return clone(defaults);
  }
}

function writeStoredConfig(next: unknown): PluginConfig {
  const normalized = normalize(next);
  window.localStorage.setItem(DEV_STORAGE_KEY, JSON.stringify(normalized));
  return normalized;
}

const devApiClient = {
  getPluginConfiguration: async () => clone(readStoredConfig()),
  updatePluginConfiguration: async (_pluginId: string, payload: unknown) => {
    writeStoredConfig(payload);
    return { updated: true };
  },
  getUrl: (path: string) => path,
  serverAddress: () => window.location.origin,
  accessToken: () => 'dev-token',
  ajax: async (options: any) => {
    const type = String(options?.type ?? 'GET').toUpperCase();
    const url = String(options?.url ?? '');

    if (!isAdminConfigPath(url)) {
      throw new Error(`Unsupported dev endpoint: ${url}`);
    }

    if (type === 'GET') {
      return clone(readStoredConfig());
    }

    if (type === 'PUT') {
      const payload =
        typeof options?.data === 'string' ? JSON.parse(options.data) : options?.data;
      return clone(writeStoredConfig(payload));
    }

    throw new Error(`Unsupported dev method: ${type}`);
  }
};

const devDashboard = {
  showLoadingMsg: () => {},
  hideLoadingMsg: () => {},
  alert: (message: string) => window.alert(message),
  processPluginConfigurationUpdateResult: () => {}
};

window.ApiClient = {
  ...window.ApiClient,
  ...devApiClient
};

window.Dashboard = {
  ...window.Dashboard,
  ...devDashboard
};

if (!window.localStorage.getItem(DEV_STORAGE_KEY)) {
  writeStoredConfig(defaults);
}

void import('./index');
