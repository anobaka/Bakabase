import {
  GM_getValue,
  GM_setValue,
  GM_addStyle,
  GM_xmlhttpRequest,
} from 'vite-plugin-monkey/dist/client';

export function getApiBaseUrl(): string {
  // The server's install endpoint seeds this value before the compiled bundle runs.
  // GM storage preserves both that initial address and user edits across CDN updates.
  return GM_getValue<string>('api_base_url', '');
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
