import { getApiBaseUrl, httpRequest } from './api';
import { normalizeApiBaseUrl, RequestError } from './requests';

export interface ConnectionState {
  connected: boolean;
  baseUrl: string;
  error: RequestError | null;
}

type Listener = (state: ConnectionState) => void;

let state: ConnectionState = { connected: false, baseUrl: '', error: null };
let started = false;
let generation = 0;
const listeners = new Set<Listener>();

const INTERVAL = 10_000; // 10s

function ping() {
  const base = getApiBaseUrl();
  const attempt = ++generation;
  // An in-flight answer from a previous address must not restore its connection.
  const update = (connected: boolean, error: RequestError | null) => {
    if (attempt === generation && base === getApiBaseUrl()) setState({ connected, baseUrl: base, error });
  };
  if (state.baseUrl !== base) update(false, null);
  if (!base) {
    update(false, new RequestError('invalid-url'));
    return;
  }
  try {
    normalizeApiBaseUrl(base);
  } catch {
    update(false, new RequestError('invalid-url'));
    return;
  }
  httpRequest<{ code?: number }>({
    method: 'GET',
    url: `${base}/tampermonkey/health`,
    timeout: 8_000,
    onSuccess: (response) => {
      if (response?.code === 0) update(true, null);
      else update(false, new RequestError('invalid-response'));
    },
    onError: (error) => update(false, error),
  });
}

function setState(value: ConnectionState) {
  if (state.connected !== value.connected || state.baseUrl !== value.baseUrl ||
    state.error?.kind !== value.error?.kind || state.error?.status !== value.error?.status ||
    state.error?.reason !== value.error?.reason) {
    state = value;
    listeners.forEach((fn) => fn(state));
  }
}

export function isConnected(): boolean {
  return state.connected;
}

export function getConnectionState(): ConnectionState {
  return state;
}

export function onConnectionChange(fn: Listener): () => void {
  listeners.add(fn);
  fn(state);
  return () => listeners.delete(fn);
}

export function startHeartbeat(): void {
  if (started) return;
  started = true;
  ping();
  setInterval(ping, INTERVAL);
}

/** Force an immediate connectivity check (e.g. after API URL change). */
export function pingNow(): void {
  ping();
}
