import type { ManagementSource } from "./api";

export interface ManagedMetadataStatus {
  identity: { serverId: string; libraryEpoch: string };
  name: string;
  data: {
    checkpoint?: {
      generation: string;
      sequence: number;
      cursor?: string;
      complete: boolean;
      catalogRevision?: number;
    };
    activeGeneration?: string;
    resourceCount: number;
    activeSequence: number;
    stagedResourceCount: number;
  };
  sync: {
    paused: boolean;
    lastSyncedAtUtc?: string;
    errorCode?: string;
    error?: string;
    isOffline: boolean;
    refreshPending?: boolean;
    catchupPending?: boolean;
  };
  denied: boolean;
}

/** The completed catalog is readable offline; files and mutations still need the owner. */
export function canReadSourceMetadata(source: Readonly<ManagementSource>) {
  if (!source.libraryEpoch) return false;
  if (source.isLocal || !source.metadata) return source.available;
  const metadata = source.metadata;

  return (
    !metadata.denied &&
    metadata.identity.serverId === source.serverId &&
    metadata.identity.libraryEpoch === source.libraryEpoch &&
    !!metadata.data.activeGeneration
  );
}

/** An explicit allowlist, shared by both transports; no files or executable actions. */
export const metadataReadOperations = new Set([
  "resourceSearch",
  "resourceGet",
  "properties",
  "customProperties",
  "getCustomPropertyByKeys",
  "libraries",
  "getAllCollections",
  "extensionGroups",
  "resourceMappings",
  "getCollectionIdsByResourceIds",
  "getResourceSourceLinks",
  "resourceScopePreferencesGet",
  "getSearchOperationsForProperty",
  "getSearchOperationsByPropertyType",
  "getFilterValueProperty",
  "propertyValueResourceCounts",
  "getResourceSearchKeywordRecommendation",
  "resourceDisplayOptions",
]);

/** Progress ticks and last-contact timestamps must not restart pagination or clear selections. */
export function sourceSearchRevision(source: Readonly<ManagementSource>) {
  return [
    source.serverId,
    source.libraryEpoch,
    source.available,
    canReadSourceMetadata(source),
    source.metadata?.data.activeGeneration,
    source.metadata?.data.activeSequence,
  ];
}

export function metadataReadError(source: Readonly<ManagementSource>) {
  return source.metadata && !source.metadata.denied && !source.metadata.data.activeGeneration
    ? "Metadata replica is not ready"
    : source.error || "Device unavailable";
}
