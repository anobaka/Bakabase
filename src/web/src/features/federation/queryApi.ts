import type { FederatedQueryPage, LocalFederatedQuery } from "./types";

import { federationRequest, jsonBody } from "./transport";

export const federationQueryApi = {
  create: (query: LocalFederatedQuery, signal?: AbortSignal) =>
    federationRequest<FederatedQueryPage>("/queries", jsonBody(query, "POST", signal)),
  page: (sessionId: string, cursor: string, signal?: AbortSignal) =>
    federationRequest<FederatedQueryPage>(
      `/queries/${encodeURIComponent(sessionId)}/pages?cursor=${encodeURIComponent(cursor)}`,
      { signal },
    ),
  release: (sessionId: string) =>
    federationRequest<unknown>(`/queries/${encodeURIComponent(sessionId)}`, {
      method: "DELETE",
      keepalive: true,
    }),
};

export type FederationQueryApi = typeof federationQueryApi;
