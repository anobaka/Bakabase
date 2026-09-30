import type { BakabaseAbstractionsModelsViewResourceMovePreviewViewModel } from "@/sdk/Api";

export type MoveResource = { id: number; path?: string | null; displayName?: string | null };
export type MoveSourceContext = { nodeId: string; libraryEpoch: string };
export type MoveResourceRef = MoveSourceContext & { resourceId: number };
export type MovePayload = {
  resources: MoveResource[];
  sourceContext?: MoveSourceContext;
  sourceTabId?: string;
  sourceTabName?: string;
  requestId?: string;
};
export type MoveDestination = {
  id: string;
  path: string;
  name?: string;
  scope: "global" | "tab";
  tabId?: string;
  order: number;
  isDeleted?: boolean;
};
export type MovePanelOptions = {
  revision?: number;
  destinations: MoveDestination[];
  autoOverwrite: boolean;
};
export type ConflictPolicy = "inherit" | "ask" | "overwrite";
export type MoveExcludedResource = {
  resourceId: number;
  displayName?: string | null;
  path?: string | null;
  reasonCode: string;
  blockingResourceIds?: number[];
};
export type MovePreview = Omit<
  BakabaseAbstractionsModelsViewResourceMovePreviewViewModel,
  "items"
> & {
  items: (BakabaseAbstractionsModelsViewResourceMovePreviewViewModel["items"][number] & {
    unavailableReason?: string | null;
    conflictKind?: string | null;
    canOverwrite?: boolean;
  })[];
  previewFingerprint?: string;
  skippedResourceIds?: number[];
  excludedResources?: MoveExcludedResource[];
  duplicateDestinationPaths?: string[];
};
export type MoveRecord = {
  id: number;
  resourceId: number;
  sourcePath: string;
  destPath: string;
  status: number;
  error?: string;
  errorCode?: string;
  conflictKind?: string;
  conflictVersion?: number;
  conflictPath?: string;
  canOverwrite?: boolean;
};
export type MoveBatch = {
  batchId: string;
  taskId?: string;
  origin?: string;
  sourceTabId?: string;
  sourceTabName?: string;
  destDir: string;
  destinationId?: string;
  destinationName?: string;
  status:
    | "queued"
    | "running"
    | "stopping"
    | "waiting"
    | "completed"
    | "partial"
    | "failed"
    | "cancelled"
    | "needsRecovery";
  percentage?: number;
  cancelRequested?: boolean;
  counts: {
    total: number;
    succeeded: number;
    failed: number;
    cancelled: number;
    skipped: number;
    waiting: number;
  };
  resourceIds?: number[];
  lockedResourceIds?: number[];
  reservedPaths?: string[];
  records: MoveRecord[];
  canRetry?: boolean;
  canCancel?: boolean;
};
export type CreateMoveRequest = {
  expectedPreviewFingerprint?: string;
  resourceIds: number[];
  resourceRefs: MoveResourceRef[];
  destDir: string;
  origin: "move-panel";
  sourceTabId?: string;
  sourceTabName?: string;
  destinationId?: string;
  destinationName?: string;
  idempotencyKey: string;
  conflictPolicy: ConflictPolicy;
};
export type MoveReservation = {
  optimistic?: boolean;
  key: string;
  phase: "preview" | "submitting" | "unknown" | "task";
  resourceIds: number[];
  paths: string[];
  batchId?: string;
};
export type MoveDraft = {
  id: string;
  payload: MovePayload;
  destination: MoveDestination;
  phase: "preview" | "ready" | "submitting" | "unknown";
  preview?: MovePreview;
  error?: string;
  contextError?: string;
  conflictPolicy: ConflictPolicy;
  request?: CreateMoveRequest;
};
