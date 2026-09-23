import type { FederationStatus, PairingResult, PathMapping } from "./types";

import { federationRequest, jsonBody } from "./transport";
import { notifyBrowsingChanged } from "./statusEvents";

const prefix = "/peers";

export const federationPeerApi = {
  status: (signal?: AbortSignal) => federationRequest<FederationStatus>(prefix, { signal }),
  browsing: async (enabled: boolean) => {
    const result = await federationRequest<unknown>(
      `${prefix}/browsing`,
      jsonBody({ enabled }, "PUT"),
    );

    notifyBrowsingChanged(enabled);

    return result;
  },
  resetIdentity: async (asNewNode: boolean) => {
    const result = await federationRequest<unknown>(
      `${prefix}/identity/reset`,
      jsonBody({ asNewNode }),
    );

    notifyBrowsingChanged(false);

    return result;
  },
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
  /** `shareBack` also offers the other device read access to this one once it approves. */
  connect: (address: string, code: string | undefined, shareBack: boolean) =>
    federationRequest<PairingResult>(`${prefix}/connect`, jsonBody({ address, code, shareBack })),
  /** The name other devices see; null restores this computer's name. */
  setName: (name: string | null) =>
    federationRequest<unknown>(`${prefix}/name`, jsonBody({ name }, "PUT")),
  claim: (requestId: string, signal?: AbortSignal) =>
    federationRequest<PairingResult>(`${prefix}/claim`, jsonBody({ requestId }, "POST", signal)),
  /** Withdraws an outgoing request that is still awaiting the other device's approval. */
  cancelRequest: (requestId: string) =>
    federationRequest<unknown>(`${prefix}/requests/${encodeURIComponent(requestId)}`, {
      method: "DELETE",
    }),
  decide: (requestId: string, approve: boolean) =>
    federationRequest<unknown>(
      `${prefix}/requests/${encodeURIComponent(requestId)}/${approve ? "approve" : "reject"}`,
      { method: "POST" },
    ),
  /** Forgets a device in both directions: access to it, its access to this device, and its record. */
  remove: (nodeId: string) =>
    federationRequest<unknown>(`${prefix}/${encodeURIComponent(nodeId)}`, { method: "DELETE" }),
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
  mappings: (nodeId: string, mappings: PathMapping[], expectedMappings: PathMapping[]) =>
    federationRequest<unknown>(
      `${prefix}/${encodeURIComponent(nodeId)}/path-mappings`,
      jsonBody({ mappings, expectedMappings }, "PUT"),
    ),
  mappingRoots: (nodeId: string, signal?: AbortSignal) =>
    federationRequest<{ sourceRootId: string; name: string }[]>(
      `${prefix}/${encodeURIComponent(nodeId)}/mapping-roots`,
      { signal },
    ),
};
