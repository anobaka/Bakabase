import { zh } from './zh';
import { en } from './en';
import { getStoredValue, setStoredValue } from '../api';
import { RequestError } from '../requests';

export type Locale = 'zh' | 'en';
export type MessageKey = keyof typeof zh;

const messages: Record<Locale, Record<MessageKey, string>> = { zh, en };

let currentLocale: Locale | null = null;
const listeners = new Set<() => void>();

function detectLocale(): Locale {
  const lang = (navigator.languages?.[0] || navigator.language || 'en').toLowerCase();
  return lang.startsWith('zh') ? 'zh' : 'en';
}

export function describeRequestError(error: unknown): string {
  if (!(error instanceof RequestError)) return error instanceof Error ? error.message : t('requestFailed');
  switch (error.kind) {
    case 'invalid-url': return t('connection.invalidUrl.description');
    case 'transport':
      if (error.reason === 'BackgroundUnavailable') return t('connection.backgroundUnavailable.description');
      return t('connection.transport.description');
    case 'permission': return t('connection.permission.description');
    case 'network':
      if (error.reason === 'ConnectionRefused') return t('connection.refused.description');
      if (error.reason === 'NameNotResolved') return t('connection.dns.description');
      if (error.reason === 'Certificate') return t('connection.certificate.description');
      return t('connection.network.description');
    case 'timeout': return t('connection.timeout.description');
    case 'aborted': return t('connection.aborted.description');
    case 'invalid-response': return t('connection.invalidResponse.description');
    case 'http':
      if (error.reason === 'Disabled') return t('connection.remoteDisabled.description');
      if (error.status === 401) return t('connection.pairingRequired.description');
      if (error.status === 403) return t('connection.forbidden.description');
      return t('connection.httpError.description', { status: error.status ?? 0 });
  }
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
