import type { TFunction } from "i18next";
import type { SyncActivity } from "./activityApi";

import { DataSyncPauseReasonLabel, DataSyncPeerErrorCodeLabel } from "@/sdk/constants";

export interface ActivityFailure {
  engine: SyncActivity["engine"];
  errorCode?: string | null;
  error?: string | null;
  sourceName?: string;
}

const definitionCodes = new Set<string>([
  ...Object.values(DataSyncPeerErrorCodeLabel),
  "ApplyFailed",
  "FetchFailed",
  "InvitationInvalid",
]);
const pauseCodes = new Set<string>(Object.values(DataSyncPauseReasonLabel));

// These are protocol/domain codes, not exception names or English message fragments.
// Keep namespaces separate: a definitions read grant is not a management grant.
export const activityErrorCodes = {
  metadata: [
    "MetadataContractChanged",
    "ManagedSourceDenied",
    "ManagementUpdateRequired",
    "MetadataGenerationChanged",
    "MetadataPageTooLarge",
    "MetadataSyncFailed",
    "MetadataRefreshPending",
    "MetadataSyncPending",
    "LibraryEpochChanged",
  ],
  processing: [
    "NoLocalFiles",
    "SourceNotManaged",
    "PathNotMapped",
    "ManagerRevoked",
    "UnsafePath",
    "DeviceUnavailable",
    "DeviceRefused",
    "Conflict",
    "ResourceChanged",
    "ResourceDeleted",
    "PropertyDeleted",
    "EpochChanged",
    "LibraryEpochChanged",
    "RuleMissing",
    "RuleEmpty",
    "InvalidRule",
    "RuleChanged",
    "CatalogChanged",
    "EnhancementFailed",
    "EnhancerUnavailable",
    "WorkerFailed",
    "CacheDisabled",
    "CleanupPending",
    "JobMissing",
    "JobNotFound",
    "JobChanged",
    "JobConflict",
    "JobCancelled",
    "AlreadyApplied",
    "PendingJobLimit",
    "InvalidJob",
    "InvalidInput",
    "InvalidKind",
    "InvalidTarget",
    "InvalidState",
    "InvalidPayload",
    "InvalidResource",
    "InvalidResult",
    "InvalidToken",
    "InvalidArtifact",
    "InvalidAttachment",
    "ArtifactChanged",
    "ArtifactConflict",
    "ArtifactHash",
    "ArtifactMissing",
    "ArtifactOffset",
    "ResultConflict",
    "BudgetExceeded",
  ],
} as const;

export function activityErrorText(t: TFunction, failure: ActivityFailure): string {
  const { engine, errorCode: code, error, sourceName: name = "" } = failure;

  if (engine === "definitions") {
    if (code === "ReadBackFailed") {
      return t("dataSync.status.ReadBackFailed", {
        name,
        reason:
          error && definitionCodes.has(error)
            ? t(`dataSync.peerError.${error}`)
            : t("dataSync.activity.error.unknown"),
      });
    }
    if (code === "PeerReset" && error === "restored")
      return t("dataSync.status.paused.PeerResetRestored", { name });
    if (code && definitionCodes.has(code)) return t(`dataSync.peerError.${code}`);
    if (code && pauseCodes.has(code)) return t(`dataSync.status.paused.${code}`, { name });
  } else if (code && (activityErrorCodes[engine] as readonly string[]).includes(code)) {
    return t(`dataSync.activity.error.${engine}.${code}`);
  }

  return t("dataSync.activity.error.unknown");
}
