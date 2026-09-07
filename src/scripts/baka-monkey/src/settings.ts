import { getStoredValue, setStoredValue } from './api';

/**
 * Per-site preferences that several components need to read and react to.
 * Values live in GM storage; listeners keep the UI in sync without a reload.
 */

const listeners = new Set<() => void>();

function notify() {
  listeners.forEach((fn) => fn());
}

export function onSettingsChange(fn: () => void): () => void {
  listeners.add(fn);
  return () => listeners.delete(fn);
}

// The overlay takes over the cover's own click target, so it stays off until the
// user asks for it, and the choice is per site: someone may want it on exhentai
// covers but not on soulplus post thumbnails.
const coverOverlayKey = (siteKey: string) => `cover_overlay_enabled.${siteKey}`;

export function isCoverOverlayEnabled(siteKey: string): boolean {
  return getStoredValue(coverOverlayKey(siteKey), false);
}

export function setCoverOverlayEnabled(siteKey: string, enabled: boolean): void {
  setStoredValue(coverOverlayKey(siteKey), enabled);
  notify();
}
