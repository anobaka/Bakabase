const legacyTimeouts = new Set([
  "Archive format discovery did not finish within its time budget. Narrow the selector or revise the plan.",
  "Archive format discovery exceeded its time limit. Revise the plan.",
  "Archive format discovery exceeded its time limit. Select a known archive or revise the plan.",
]);

export const archiveDiscoveryReasons = new Set([
  "ArchiveProbeTimedOut",
  "ArchiveDetectionBudgetExceeded",
  "ArchiveProbeOutputLimit",
  "ArchiveProbePrefixLimit",
  "ArchiveProbeToolFailed",
  "ArchiveProbeUpgradeRequired",
  "ArchiveProbeIncomplete",
  "ProcessingInputNotMatched",
  "ArchiveInputRequired",
]);

export const archiveCompatibilityReasons = new Set([
  "ArchiveNameEncodingUnsupported",
  "ArchivePasswordEncodingUnsupported",
  "ArchivePasswordNotRepresentable",
  "ArchiveIntegrityFailure",
  "ArchiveGroupInputChanged",
  "ArchiveExpansionLimit",
  "ArchiveUnsafeOutput",
  "ArchiveStorageUnavailable",
  "DEPENDENCY_REQUIRED",
]);

export const fileProcessingRecoveryReasons = new Set([
  ...archiveDiscoveryReasons,
  ...archiveCompatibilityReasons,
  "ProcessingPathConflict",
]);

/** Only exact diagnostics emitted by older servers are compatible; arbitrary text stays manual. */
export function fileProcessingFailureReason(reason?: string, message?: string): string | undefined {
  if (
    reason === "ExtractionPlanUnknown" &&
    message ===
      "No supported archive was recognized inside the selected media-prefixed file. Revise the extraction step."
  )
    return "ArchiveProbeUpgradeRequired";
  return reason === "ExtractionPlanUnknown" && message && legacyTimeouts.has(message)
    ? "ArchiveProbeTimedOut"
    : reason;
}

export function canRetryFileProcessing(reason?: string): boolean {
  return (
    reason === "ArchiveProbeTimedOut" ||
    reason === "ArchiveProbeToolFailed" ||
    reason === "ArchiveProbeUpgradeRequired" ||
    reason === "ArchiveProbePrefixLimit" ||
    reason === "DEPENDENCY_REQUIRED" ||
    reason === "ArchiveStorageUnavailable"
  );
}
