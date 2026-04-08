import { zh } from './zh';
import { en } from './en';
import { getStoredValue, setStoredValue } from '../api';

export type Locale = 'zh' | 'en';
export type MessageKey = keyof typeof zh;

const messages: Record<Locale, Record<MessageKey, string>> = { zh, en };

let currentLocale: Locale | null = null;
const listeners = new Set<() => void>();

function detectLocale(): Locale {
  const lang = navigator.language.toLowerCase();
  return lang.startsWith('zh') ? 'zh' : 'en';
}

export function getLocale(): Locale {
  if (currentLocale === null) {
    const stored = getStoredValue<string>('locale', '');
    currentLocale = (stored === 'zh' || stored === 'en') ? stored : detectLocale();
  }
  return currentLocale;
}

export function setLocale(locale: Locale): void {
  currentLocale = locale;
  setStoredValue('locale', locale);
  listeners.forEach((fn) => fn());
}

export function onLocaleChange(fn: () => void): () => void {
  listeners.add(fn);
  return () => listeners.delete(fn);
}

export function t(key: MessageKey, params?: Record<string, string | number>): string {
  let text = messages[getLocale()][key] ?? key;
  if (params) {
    for (const [k, v] of Object.entries(params)) {
      text = text.replace(`{${k}}`, String(v));
    }
  }
  return text;
}
