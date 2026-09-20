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

export const resourceKey = (ref: ResourceRef) =>
  JSON.stringify([ref.nodeId, ref.libraryEpoch, ref.resourceId]);

/** Never resolve a ref against the current device just because its numeric ID exists here. */
export const sameResource = (a: ResourceRef, b: ResourceRef) => resourceKey(a) === resourceKey(b);
