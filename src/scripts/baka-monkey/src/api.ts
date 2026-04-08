import {
  GM_getValue,
  GM_setValue,
  GM_addStyle,
  GM_xmlhttpRequest,
} from 'vite-plugin-monkey/dist/client';

const DEFAULT_API_URL = '';

export function getApiBaseUrl(): string {
  const stored = GM_getValue<string>('api_base_url', '');
  if (stored) return stored;
  // API install path injects a non-empty DEFAULT_API_URL; persist it so
  // it survives auto-updates from CDN (where the default is empty).
  if (DEFAULT_API_URL) {
    GM_setValue('api_base_url', DEFAULT_API_URL);
    return DEFAULT_API_URL;
  }
  return '';
}

export function setApiBaseUrl(url: string): void {
  GM_setValue('api_base_url', url);
}

export function getStoredValue<T>(key: string, defaultValue: T): T {
  return GM_getValue(key, defaultValue);
}

export function setStoredValue(key: string, value: unknown): void {
  GM_setValue(key, value);
}

export function addStyle(css: string): void {
  GM_addStyle(css);
}

export function httpRequest<T = unknown>(options: {
  method: string;
  url: string;
  data?: unknown;
  onSuccess?: (data: T) => void;
  onError?: (error: unknown) => void;
}): void {
  GM_xmlhttpRequest({
    method: options.method,
    url: options.url,
    headers: { 'Content-Type': 'application/json' },
    data: options.data ? JSON.stringify(options.data) : undefined,
    onload(response) {
      if (response.status === 200) {
        const result = JSON.parse(response.responseText);
        options.onSuccess?.(result as T);
      } else {
        options.onError?.(response);
      }
    },
    onerror(error) {
      options.onError?.(error);
    },
  });
}
