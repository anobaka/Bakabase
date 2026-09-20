import type { OmittedNode } from "./types";

import envConfig from "@/config/env";

export class FederationError extends Error {
  constructor(
    public readonly code: string,
    message: string,
    public readonly status: number,
    public readonly retryable: boolean = false,
    public readonly nodeId?: string,
    public readonly omittedNodes: OmittedNode[] = [],
  ) {
    super(message);
    this.name = "FederationError";
  }
}

export const isAbort = (error: unknown) => error instanceof Error && error.name === "AbortError";

/** The only transport used by the new views. It never selects or changes the legacy BApi target. */
export async function federationRequest<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${envConfig.apiEndpoint}/federation/local${path}`, {
    ...init,
    cache: "no-store",
    headers: {
      ...(init?.body ? { "Content-Type": "application/json" } : {}),
      ...init?.headers,
    },
  });
  const payload = await response.json().catch(() => undefined);

  if (!response.ok) {
    const error = payload?.error ?? payload;

    throw new FederationError(
      typeof error?.code === "string" ? error.code : `Http${response.status}`,
      error?.message ?? response.statusText,
      response.status,
      error?.retryable ?? false,
      error?.nodeId,
      Array.isArray(error?.omittedNodes) ? error.omittedNodes : [],
    );
  }

  return payload as T;
}

export const jsonBody = (body: unknown, method = "POST", signal?: AbortSignal): RequestInit => ({
  method,
  body: JSON.stringify(body),
  signal,
});
