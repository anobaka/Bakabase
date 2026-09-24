import type {
  BakabaseModulesFederationContractsCommonLibraryQuery,
  BakabaseModulesFederationContractsFederatedQueryPage,
  BakabaseModulesFederationContractsFederatedResourceSummary,
  BakabaseModulesFederationContractsLocalFederatedQuery,
  BakabaseModulesFederationContractsQueryOmittedNode,
  BakabaseModulesFederationContractsResourceRef,
  BakabaseModulesFederationMediaFederatedAsset,
  BakabaseModulesFederationMediaFederatedProperty,
  BakabaseModulesFederationMediaFederatedResourceDetail,
  BakabaseModulesFederationMediaPlaybackSessionResponse,
  BakabaseModulesFederationPeersFederationPeerView,
  BakabaseModulesFederationPeersNodePairingOutcome,
  BakabaseModulesFederationPeersNodePairingRequestView,
  BakabaseModulesFederationPeersNodePathMapping,
  BakabaseServiceControllersFederationPeerStatusResponse,
} from "@/sdk/Api";
import type { ManagedServerOutcome, ManagedServerState, RemoteAccessMode } from "@/sdk/constants";

/** Generated wire DTOs remain tied to the backend; only supported input choices are narrowed here. */
export type ResourceRef = BakabaseModulesFederationContractsResourceRef;
export type CommonLibraryQuery = Pick<
  BakabaseModulesFederationContractsCommonLibraryQuery,
  "text" | "sourceKinds"
> & {
  queryContractVersion: 1;
  fileAvailability?: "Any" | "HasFile" | "MetadataOnly";
  sort: "NameAsc" | "NameDesc";
};
export type LocalFederatedQuery = Pick<
  BakabaseModulesFederationContractsLocalFederatedQuery,
  "nodeIds" | "pageSize"
> & { query: CommonLibraryQuery };
export type FederatedResourceSummary = BakabaseModulesFederationContractsFederatedResourceSummary;
export type OmittedNode = BakabaseModulesFederationContractsQueryOmittedNode;
export type FederatedQueryPage = BakabaseModulesFederationContractsFederatedQueryPage;
export type PathMapping = BakabaseModulesFederationPeersNodePathMapping;
export type Peer = BakabaseModulesFederationPeersFederationPeerView;
export type PairingRequest = BakabaseModulesFederationPeersNodePairingRequestView;
export type FederationStatus = BakabaseServiceControllersFederationPeerStatusResponse;
export type PairingResult = BakabaseModulesFederationPeersNodePairingOutcome;
export type FederatedAsset = BakabaseModulesFederationMediaFederatedAsset;
export type FederatedResourceDetail = Omit<
  BakabaseModulesFederationMediaFederatedResourceDetail,
  "properties"
> & {
  properties: (Omit<BakabaseModulesFederationMediaFederatedProperty, "value"> & {
    value?: unknown;
  })[];
};
export type PlaybackSession = BakabaseModulesFederationMediaPlaybackSessionResponse;

/*
 * Servers this device manages in full (`/federation/local/servers`).
 *
 * Hand-typed against `ManagedServerModels.cs` rather than aliased to the generated
 * DTOs: those spell the enums as bare number unions, and these screens branch on them,
 * so the named enums from `constants.ts` keep the comparisons readable. The wire is
 * camelCase with enums as numbers, so the shapes are identical.
 */

/** Where one of a managed server's library paths is on this machine. */
export interface ManagedServerPathMapping {
  serverPath: string;
  localPath: string;
}

/** A server this device can switch its window to. Never carries the key. */
export interface ManagedServer {
  serverId: string;
  name?: string | null;
  address: string;
  pairedAt: string;
  lastConnectedAt?: string | null;
  pathMappings: ManagedServerPathMapping[];
  state: ManagedServerState;
  /** When last probed. `Unrestricted` means anybody on its network can manage it. */
  mode?: RemoteAccessMode | null;
  appVersion?: string | null;
  importedFromLegacyClient: boolean;
}

/**
 * A management request this device filed: one it is still waiting on, or one that has
 * just ended and is kept a while so the page can say how.
 */
export interface ManagedServerPendingRequest {
  requestId: string;
  address: string;
  serverName?: string | null;
  expiresAt: string;
  /**
   * The last thing asking about it produced. While {@link active} this can be a failed
   * attempt (`Unreachable`, `TooManyAttempts`) the app is retrying, not an answer.
   */
  outcome: ManagedServerOutcome;
  /**
   * True while the app still waits on it and keeps asking. False once it was approved,
   * rejected, expired or cancelled — only then is {@link outcome} final.
   */
  active: boolean;
}

export interface ManagedServersView {
  /** False where nothing can be managed from here (a headless server). */
  available: boolean;
  servers: ManagedServer[];
  requests: ManagedServerPendingRequest[];
}

export interface ManagedServerProbe {
  outcome: ManagedServerOutcome;
  serverId?: string | null;
  name?: string | null;
  appVersion?: string | null;
  mode?: RemoteAccessMode | null;
  pairingSupported: boolean;
  alreadyManaged: boolean;
  detail?: string | null;
}

export interface ManagedServerPairing {
  outcome: ManagedServerOutcome;
  serverId?: string | null;
  serverName?: string | null;
  /** Set while a filed request waits for approval; the app collects the answer itself. */
  requestId?: string | null;
  expiresAt?: string | null;
  detail?: string | null;
}

/**
 * A Bakabase server announcing itself on this network, found by its remote-access beacon —
 * the one every server sends, whether or not it shares its library.
 */
export interface ManagedServerCandidate {
  serverId: string;
  name: string;
  address: string;
  appVersion: string;
  /** Already managed from here: listed so the user sees it was found, not to add again. */
  alreadyManaged: boolean;
}

export interface ManagedServerDiscovery {
  /** Never this installation itself. */
  servers: ManagedServerCandidate[];
}

export interface ManagedServerImport {
  /** Whether a Bakabase Client installation with pairings exists on this machine. */
  found: boolean;
  imported: number;
  skipped: number;
}

export const resourceKey = (ref: ResourceRef) =>
  JSON.stringify([ref.nodeId, ref.libraryEpoch, ref.resourceId]);

/** Never resolve a ref against the current device just because its numeric ID exists here. */
export const sameResource = (a: ResourceRef, b: ResourceRef) => resourceKey(a) === resourceKey(b);
