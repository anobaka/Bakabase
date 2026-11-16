import type { TFunction } from "i18next";

export const deletionReasonCodes = new Set([
  "ACQUISITION_TASK_ACTIVE", "ACQUISITION_DOWNLOAD_ACTIVE", "ACQUISITION_TASK_CHANGED",
  "ACQUISITION_TASK_DELETED", "ACQUISITION_TASK_NOT_FOUND", "ACQUISITION_DOWNLOAD_STATE_UNKNOWN",
  "ACQUISITION_EXECUTION_STATE_UNKNOWN", "CLEANUP_TASK_NOT_COMPLETED", "CLEANUP_DELIVERY_PROOF_MISSING",
  "CLEANUP_DELIVERY_ACCEPTANCE_REQUIRED", "CLEANUP_FILES_RETAINED", "CLEANUP_PREVIEW_FAILED",
  "DeliveryAcceptanceChanged", "SharedOwner", "ReferencedArtifact", "ReferencedPath", "ReferencedDescendant",
  "ActiveOwner", "ActiveConsumer", "UnboundArtifact", "InputUnavailable", "ResultNotVerified",
  "SelectionChanged", "WorkspaceChanged", "WorkspaceUnavailable", "RetentionPolicyKeep",
  "IntermediateResultRetained", "OperationNotCompleted", "NotManagedInput", "NotManagedProcessedArtifact",
  "BoundaryOwnerUnconfirmed", "ArtifactMissing", "UnrelatedArtifact", "FILE_CLEANUP_COVERAGE_INCOMPLETE",
  "CLEANUP_OLD_EXECUTION_REQUIRES_REVIEW", "CLEANUP_RETIRED_WORKSPACE_REQUIRES_REVIEW",
  "CLEANUP_PATH_NOT_AUTHORIZED",
  "CLEANUP_DIRECTORY_OWNERSHIP_UNCONFIRMED",
  "AlreadyAbsent", "CLEANUP_TASK_NOT_DELETED", "CLEANUP_ONLY_RETIRED_WORKSPACE_RECORDS",
]);

/** Never expose raw upstream errors, paths or untranslated future reason codes. */
export const deletionReasonLabel = (t: TFunction, code?: string | null) =>
  t<string>(`acquisition.delete.reason.${code && deletionReasonCodes.has(code) ? code : "unknown"}`);
