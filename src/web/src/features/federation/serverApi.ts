import type {
  ManagedServerDiscovery,
  ManagedServerPairing,
  ManagedServerPathMapping,
  ManagedServerProbe,
  ManagedServersView,
} from "./types";

import { FederationError, federationRequest, jsonBody } from "./transport";

export interface ManagedDeviceName {
  name: string;
  libraryEpoch: string;
  environmentControlled: boolean;
}

const prefix = "/servers";
const server = (serverId: string) => `${prefix}/${encodeURIComponent(serverId)}`;

/**
 * Servers this device manages in full, each shown in this window through a signed
 * loopback relay.
 *
 * Deliberately beside {@link federationPeerApi} rather than inside it: a peer is a
 * read-only grant between two libraries, a managed server is the other install's own
 * administrator access. The two never share a credential, so they do not share a client.
 *
 * Local-only like every `/federation/local` route, which is why it goes through
 * {@link federationRequest} and never through the legacy `BApi` target.
 */
export const managedServerApi = {
  /** `probe` also asks every server how it is — bounded server-side to a couple of seconds. */
  list: (probe = false, signal?: AbortSignal) =>
    federationRequest<ManagedServersView>(probe ? `${prefix}?probe=true` : prefix, { signal }),
  /**
   * Servers announcing themselves nearby, from their remote-access beacons. Takes a few
   * seconds — it listens rather than asks.
   *
   * Not {@link federationPeerApi}'s discovery: that one only lists devices that share
   * their library, which a server that can be managed usually does not.
   */
  discover: (signal?: AbortSignal) =>
    federationRequest<ManagedServerDiscovery>(`${prefix}/discover`, { signal }),
  /** What an address is, before anything is paired. Never throws for an unreachable address. */
  probe: (address: string) =>
    federationRequest<ManagedServerProbe>(`${prefix}/probe`, jsonBody({ address })),
  /** With a code shown on the other server; without one, files a request for it to approve. */
  pair: (address: string, code?: string) =>
    federationRequest<ManagedServerPairing>(
      `${prefix}/pair`,
      jsonBody(code ? { address, code } : { address }),
    ),
  cancelRequest: (requestId: string) =>
    federationRequest<{ changed: boolean }>(`${prefix}/requests/${encodeURIComponent(requestId)}`, {
      method: "DELETE",
    }),
  /** Asks the server to revoke this device (best effort), then deletes the key here. */
  forget: (serverId: string) =>
    federationRequest<{ changed: boolean }>(server(serverId), { method: "DELETE" }),
  /** Reads the owner's effective name and captures its library incarnation before editing. */
  getDeviceName: async (serverId: string, signal?: AbortSignal) => {
    const { sources } = await federationRequest<{
      sources: {
        serverId: string;
        libraryEpoch?: string;
        available: boolean;
        errorCode?: string;
        error?: string;
      }[];
    }>("/management/sources", { signal });
    const owner = sources.find((source) => source.serverId === serverId);

    if (!owner?.available || !owner.libraryEpoch)
      throw new FederationError(
        owner?.errorCode ?? "ManagedSourceUnavailable",
        owner?.error ?? "",
        503,
      );

    return federationRequest<ManagedDeviceName>(
      `/management/${encodeURIComponent(serverId)}/request`,
      jsonBody({ libraryEpoch: owner.libraryEpoch, operation: "deviceNameGet" }, "POST", signal),
    );
  },
  /** A global rename on that device, not an alias in this one's connection list. */
  setDeviceName: (serverId: string, snapshot: ManagedDeviceName, name: string | null) =>
    federationRequest<ManagedDeviceName>(
      `/management/${encodeURIComponent(serverId)}/request`,
      jsonBody({
        libraryEpoch: snapshot.libraryEpoch,
        operation: "deviceNameSet",
        body: { name, expectedName: snapshot.name },
      }),
    ),
  setPathMappings: (serverId: string, mappings: ManagedServerPathMapping[]) =>
    federationRequest<{ changed: boolean }>(
      `${server(serverId)}/path-mappings`,
      jsonBody({ mappings }, "PUT"),
    ),
  /**
   * Starts the server's relay if needed and answers where the window should go.
   *
   * Always sends a body, even an empty one: the endpoint binds it from JSON, and a
   * bodiless POST is a content-type question the server should not have to answer.
   */
  open: (serverId: string, path?: string) =>
    federationRequest<{ url: string }>(`${server(serverId)}/open`, jsonBody(path ? { path } : {})),
};
