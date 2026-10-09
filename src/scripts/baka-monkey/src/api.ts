import {
  GM_getValue,
  GM_setValue,
  GM_addStyle,
  GM_xmlhttpRequest,
} from 'vite-plugin-monkey/dist/client';
import { normalizeApiBaseUrl, sendRequest, type RequestOptions } from './requests';

let apiAddressGeneration = 0;

/** Every address change invalidates pending results, even a quick A → B → A. */
export function getApiAddressGeneration(): number {
  return apiAddressGeneration;
}

export function getApiBaseUrl(): string {
  // The server's install endpoint seeds this value before the compiled bundle runs.
  // GM storage preserves both that initial address and user edits across CDN updates.
  const stored = GM_getValue<string>('api_base_url', '');
  if (!stored) return '';
  try {
    // Older versions persisted pasted URLs, including their trailing slash.
    return normalizeApiBaseUrl(stored);
  } catch {
    return stored;
  }
}

export function setApiBaseUrl(url: string): string {
  const normalized = normalizeApiBaseUrl(url);
  if (normalized !== getApiBaseUrl()) apiAddressGeneration++;
  GM_setValue('api_base_url', normalized);
  return normalized;
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

export function httpRequest<T = unknown>(options: RequestOptions<T>): void {
  sendRequest(GM_xmlhttpRequest, options);
}
