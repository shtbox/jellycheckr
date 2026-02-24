export function getHostWindow(): Window {
  try {
    if (window.parent && window.parent !== window) {
      return window.parent as Window;
    }
  } catch {
    // Ignore cross-frame access errors; fall back to current window.
  }

  return window;
}

export function getApiClient() {
  const host = getHostWindow();
  return window.ApiClient || host.ApiClient;
}

export function getDashboard() {
  const host = getHostWindow();
  return window.Dashboard || host.Dashboard;
}

function toAbsoluteUrl(path: string): string {
  const apiClient = getApiClient();
  if (apiClient?.getUrl) {
    try {
      return apiClient.getUrl(path);
    } catch {
      // Fall through to other URL builders.
    }
  }

  const serverAddress = apiClient?.serverAddress?.();
  if (serverAddress) {
    const base = serverAddress.replace(/\/+$/, '');
    const normalized = path.startsWith('/') ? path : `/${path}`;
    return `${base}${normalized}`;
  }

  return path.startsWith('/') ? path : `/${path}`;
}

function getAuthHeaders(): Record<string, string> {
  const token = getApiClient()?.accessToken?.();
  if (!token) {
    return {};
  }

  return {
    'X-Emby-Token': token,
    'X-MediaBrowser-Token': token
  };
}

function tryParseJson(payload: any): any {
  if (typeof payload !== 'string') {
    return payload;
  }

  try {
    return JSON.parse(payload);
  } catch {
    return payload;
  }
}

export async function getJson(path: string): Promise<any> {
  const apiClient = getApiClient();
  const url = toAbsoluteUrl(path);

  if (apiClient?.ajax) {
    try {
      const response = await apiClient.ajax({
        type: 'GET',
        url,
        dataType: 'json'
      });
      return tryParseJson(response);
    } catch {
      // Fall back to fetch.
    }
  }

  const response = await fetch(url, {
    method: 'GET',
    headers: {
      Accept: 'application/json',
      ...getAuthHeaders()
    },
    credentials: 'same-origin'
  });

  if (!response.ok) {
    throw new Error(`GET ${path} failed with ${response.status}`);
  }

  return response.json();
}

export async function putJson(path: string, payload: unknown): Promise<any> {
  const apiClient = getApiClient();
  const url = toAbsoluteUrl(path);

  if (apiClient?.ajax) {
    try {
      const response = await apiClient.ajax({
        type: 'PUT',
        url,
        dataType: 'json',
        contentType: 'application/json',
        data: JSON.stringify(payload)
      });
      return tryParseJson(response);
    } catch {
      // Fall back to fetch.
    }
  }

  const response = await fetch(url, {
    method: 'PUT',
    headers: {
      'Content-Type': 'application/json',
      Accept: 'application/json',
      ...getAuthHeaders()
    },
    credentials: 'same-origin',
    body: JSON.stringify(payload)
  });

  if (!response.ok) {
    throw new Error(`PUT ${path} failed with ${response.status}`);
  }

  return response.json();
}
