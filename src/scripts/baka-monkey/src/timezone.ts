import { getStoredValue, setStoredValue } from './api';

const STORAGE_KEY = 'timezone';

/** Special preference value meaning "follow the browser's detected timezone". */
export const AUTO_TIMEZONE = 'auto';

let currentPreference: string | null = null;
const listeners = new Set<() => void>();

/** The browser's detected IANA timezone, or 'UTC' when it can't be determined. */
export function getBrowserTimeZone(): string {
  try {
    const tz = Intl.DateTimeFormat().resolvedOptions().timeZone;
    if (tz) return tz;
  } catch {
    /* Intl not available – fall through */
  }
  return 'UTC';
}

/**
 * The stored preference: `AUTO_TIMEZONE` (follow the browser) or a specific
 * IANA name the user picked in the settings panel.
 */
export function getTimeZonePreference(): string {
  if (currentPreference === null) {
    const stored = getStoredValue<string>(STORAGE_KEY, '');
    currentPreference = stored || AUTO_TIMEZONE;
  }
  return currentPreference;
}

/** The IANA timezone actually used for formatting (resolves `auto`). */
export function getEffectiveTimeZone(): string {
  const pref = getTimeZonePreference();
  return pref === AUTO_TIMEZONE ? getBrowserTimeZone() : pref;
}

export function setTimeZonePreference(pref: string): void {
  currentPreference = pref;
  setStoredValue(STORAGE_KEY, pref);
  listeners.forEach((fn) => fn());
}

export function onTimeZoneChange(fn: () => void): () => void {
  listeners.add(fn);
  return () => listeners.delete(fn);
}

const FALLBACK_TIMEZONES = [
  'UTC',
  'Asia/Shanghai', 'Asia/Hong_Kong', 'Asia/Taipei', 'Asia/Tokyo', 'Asia/Seoul',
  'Asia/Singapore', 'Asia/Bangkok', 'Asia/Kolkata', 'Asia/Dubai',
  'Europe/London', 'Europe/Paris', 'Europe/Berlin', 'Europe/Moscow',
  'America/New_York', 'America/Chicago', 'America/Denver', 'America/Los_Angeles',
  'America/Sao_Paulo', 'Australia/Sydney',
];

/** All IANA timezones the browser exposes, or a small curated fallback list. */
export function getSupportedTimeZones(): string[] {
  try {
    const supportedValuesOf = (Intl as unknown as {
      supportedValuesOf?: (key: string) => string[];
    }).supportedValuesOf;
    if (typeof supportedValuesOf === 'function') {
      const values = supportedValuesOf('timeZone');
      if (values?.length) return values;
    }
  } catch {
    /* not supported – fall through */
  }
  return FALLBACK_TIMEZONES;
}
