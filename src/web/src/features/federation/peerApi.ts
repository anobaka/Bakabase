import type { FederationStatus, PairingResult, PathMapping } from "./types";

import { federationRequest, jsonBody } from "./transport";

const prefix = "/peers";

export const federationPeerApi = {
  status: (signal?: AbortSignal) => federationRequest<FederationStatus>(prefix, { signal }),
  resetIdentity: () =>
    federationRequest<unknown>(`${prefix}/identity/reset`, jsonBody({ asNewNode: true })),
  discover: (signal?: AbortSignal) =>
    federationRequest<{ nodeId: string; name: string; address: string }[]>(`${prefix}/discover`, {
      signal,
    }),
  sharing: (enabled: boolean, enablePairedRemoteAccess: boolean) =>
    federationRequest<unknown>(
      `${prefix}/sharing`,
      jsonBody({ enabled, enablePairedRemoteAccess }, "PUT"),
    ),
  invite: () =>
    federationRequest<{ code: string; expiresAt: string }>(`${prefix}/invite`, { method: "POST" }),
  connect: (address: string, code?: string) =>
    federationRequest<PairingResult>(`${prefix}/connect`, jsonBody({ address, code })),
  claim: (requestId: string) =>
    federationRequest<PairingResult>(`${prefix}/claim`, jsonBody({ requestId })),
  decide: (requestId: string, approve: boolean) =>
    federationRequest<unknown>(
      `${prefix}/requests/${encodeURIComponent(requestId)}/${approve ? "approve" : "reject"}`,
      { method: "POST" },
    ),
  forget: (nodeId: string) =>
    federationRequest<unknown>(`${prefix}/${encodeURIComponent(nodeId)}/outbound`, {
      method: "DELETE",
    }),
  revoke: (grantId: string) =>
    federationRequest<unknown>(`${prefix}/grants/${encodeURIComponent(grantId)}`, {
      method: "DELETE",
    }),
  enable: (nodeId: string, enabled: boolean) =>
    federationRequest<unknown>(
      `${prefix}/${encodeURIComponent(nodeId)}/enabled`,
      jsonBody({ enabled }, "PUT"),
    ),
  mappings: (nodeId: string, mappings: PathMapping[]) =>
    federationRequest<unknown>(
      `${prefix}/${encodeURIComponent(nodeId)}/path-mappings`,
      jsonBody({ mappings }, "PUT"),
    ),
  mappingRoots: (nodeId: string, signal?: AbortSignal) =>
    federationRequest<{ sourceRootId: string; name: string }[]>(
      `${prefix}/${encodeURIComponent(nodeId)}/mapping-roots`,
      { signal },
    ),
};
