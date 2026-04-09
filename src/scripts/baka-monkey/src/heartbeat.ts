import { getApiBaseUrl, httpRequest } from './api';

type Listener = (connected: boolean) => void;

let connected = false;
let started = false;
const listeners = new Set<Listener>();

const INTERVAL = 10_000; // 10s

function ping() {
  const base = getApiBaseUrl();
  if (!base) {
    setConnected(false);
    return;
  }
  httpRequest({
    method: 'GET',
    url: `${base}/tampermonkey/health`,
    onSuccess: () => setConnected(true),
    onError: () => setConnected(false),
  });
}

function setConnected(value: boolean) {
  if (connected !== value) {
    connected = value;
    listeners.forEach((fn) => fn(value));
  }
}

export function isConnected(): boolean {
  return connected;
}

export function onConnectionChange(fn: Listener): () => void {
  listeners.add(fn);
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
