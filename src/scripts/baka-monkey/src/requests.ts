export type RequestErrorKind = 'invalid-url' | 'transport' | 'permission' | 'network' | 'timeout' | 'aborted' | 'http' | 'invalid-response';

export class RequestError extends Error {
  readonly kind: RequestErrorKind;
  readonly status?: number;
  readonly reason?: string;

  constructor(kind: RequestErrorKind, status?: number, reason?: string) {
    super(status ? `HTTP ${status}` : kind);
    this.name = 'RequestError';
    this.kind = kind;
    this.status = status;
    this.reason = reason;
  }
}

/** Only an origin is supported, matching the server's userscript installer. */
export function normalizeApiBaseUrl(value: string): string {
  const input = value.trim();
  if (!/^https?:\/\/[^/?#\\\s]+\/?$/i.test(input)) throw new RequestError('invalid-url');
  let url: URL;
  try {
    url = new URL(input);
  } catch {
    throw new RequestError('invalid-url');
  }
  if (url.username || url.password || !url.hostname || ['0.0.0.0', '[::]', '*', '+'].includes(url.hostname) || url.port === '0') {
    throw new RequestError('invalid-url');
  }
  return url.origin;
}

export interface RequestOptions<T> {
  method: string;
  url: string;
  data?: unknown;
  timeout?: number;
  onSuccess?: (data: T) => void;
  onError?: (error: RequestError) => void;
}

interface Response {
  status: number;
  responseText: string;
  responseHeaders?: string;
}

export interface RequestDetails {
  method: string;
  url: string;
  headers: Record<string, string>;
  data?: string;
  timeout: number;
  onload: (response: Response) => void;
  onerror: (error?: unknown) => void;
  ontimeout: () => void;
  onabort: () => void;
}

type Transport = (details: RequestDetails) => { abort(): void } | void;

function classifyTransportError(error: unknown, fallback: 'network' | 'transport'): RequestError {
  let diagnostic = '';
  let status: number | undefined;
  if (typeof error === 'string') diagnostic = error;
  else if (error && typeof error === 'object') {
    const event = error as Record<string, unknown>;
    diagnostic = ['error', 'message', 'statusText'].map((key) => typeof event[key] === 'string' ? event[key] : '').join(' ');
    if (typeof event.status === 'number') status = event.status;
  }
  // Only emit a fixed category. Extension errors may contain URLs or credentials.
  if (/@connect|whitelist|permission|not permitted|not allowed|access.denied|forbidden.domain|blocked.by.(?:client|the.user)|permanently.blocked/i.test(diagnostic)) {
    return new RequestError('permission', status);
  }
  if (/background.shutdown/i.test(diagnostic)) return new RequestError('transport', status, 'BackgroundUnavailable');
  if (/timed?\s*out|timeout/i.test(diagnostic)) return new RequestError('timeout', status);
  if (/connection.refused|ERR_CONNECTION_REFUSED/i.test(diagnostic)) return new RequestError('network', status, 'ConnectionRefused');
  if (/name.not.resolved|unknown.host|ENOTFOUND/i.test(diagnostic)) return new RequestError('network', status, 'NameNotResolved');
  if (/certificate|ERR_CERT_|ssl|tls/i.test(diagnostic)) return new RequestError('network', status, 'Certificate');
  return new RequestError(fallback, status);
}

/** GM requests run in the extension, so page Network tools may show no request. */
export function sendRequest<T>(transport: Transport | undefined, options: RequestOptions<T>): void {
  let settled = false;
  let watchdog: ReturnType<typeof setTimeout> | undefined;
  let safeUrl: string | undefined;
  const fail = (error: RequestError) => {
    if (settled) return;
    settled = true;
    clearTimeout(watchdog);
    // Do not log request bodies, cookies, or arbitrary server responses.
    console.error(`[Bakabase] ${options.method} request failed`, { url: safeUrl, kind: error.kind, status: error.status, reason: error.reason });
    options.onError?.(error);
  };

  let url: URL;
  try {
    url = new URL(options.url);
    if (!['http:', 'https:'].includes(url.protocol) || url.username || url.password) throw new Error();
  } catch {
    fail(new RequestError('invalid-url'));
    return;
  }
  safeUrl = `${url.origin}${url.pathname}`;
  if (typeof transport !== 'function') {
    fail(new RequestError('transport'));
    return;
  }

  const timeout = options.timeout ?? 15_000;
  let handle: ReturnType<Transport>;
  // Some extension failures (including unanswered permission prompts) never
  // invoke a callback. Bound those too, then ignore late callbacks.
  watchdog = setTimeout(() => {
    fail(new RequestError('timeout'));
    try {
      handle?.abort();
    } catch {
      // A broken extension abort handle must not obscure the timeout already reported.
    }
  }, timeout);
  console.debug(`[Bakabase] ${options.method} ${safeUrl}`);
  try {
    handle = transport({
      method: options.method,
      url: url.href,
      headers: { 'Content-Type': 'application/json' },
      data: options.data === undefined ? undefined : JSON.stringify(options.data),
      timeout,
      onload(response) {
        if (settled) return;
        if (response.status < 200 || response.status >= 300) {
          const reason = /^X-Bakabase-Remote-Access:\s*(\S+)/im.exec(response.responseHeaders ?? '')?.[1];
          fail(new RequestError(response.status === 0 ? 'network' : 'http', response.status, reason));
          return;
        }
        let result: T;
        try {
          result = JSON.parse(response.responseText) as T;
          if (result === null || typeof result !== 'object' || Array.isArray(result)) throw new Error();
        } catch {
          fail(new RequestError('invalid-response'));
          return;
        }
        settled = true;
        clearTimeout(watchdog);
        options.onSuccess?.(result);
      },
      onerror: (error) => fail(classifyTransportError(error, 'network')),
      ontimeout: () => fail(new RequestError('timeout')),
      onabort: () => fail(new RequestError('aborted')),
    });
  } catch (error) {
    fail(classifyTransportError(error, 'transport'));
  }
}
