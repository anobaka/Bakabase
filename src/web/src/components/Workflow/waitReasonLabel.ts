import type { TFunction } from "i18next";
import { localStorageProblemCode } from "@/components/CloudDownload/problemLabels";
import { fileProcessingRecoveryReasons, fileProcessingFailureReason } from "./fileProcessingFailure";

/** Only this typed cleanup diagnostic changes the guidance; never show arbitrary prompt text. */
export function fileCleanupCoverageIncomplete(waitPromptJson?: string | null): boolean {
  try {
    const prompt = JSON.parse(waitPromptJson ?? "{}");
    const message = prompt?.message ?? prompt?.Message;
    return typeof message === "string" &&
      /^FILE_CLEANUP_COVERAGE_INCOMPLETE(?:$|[:\s])/.test(message);
  } catch {
    return false;
  }
}

/** A processing suspension has one actionable cause; its category is not evidence of a missing password. */
export function workflowWaitReasonLabel(
  t: TFunction,
  waitReason?: string | null,
  waitPromptJson?: string | null,
): string {
  let reason = waitReason;
  if (reason === "artifacts.cleanup" && fileCleanupCoverageIncomplete(waitPromptJson))
    reason = "FILE_CLEANUP_COVERAGE_INCOMPLETE";
  if (reason === "artifacts.publication") {
    try {
      const prompt = JSON.parse(waitPromptJson ?? "{}");
      const code = localStorageProblemCode(prompt?.reason ?? prompt?.Reason) ??
        localStorageProblemCode(prompt?.message ?? prompt?.Message);
      if (code === "LOCAL_STORAGE_RESERVE_LOW") reason = code;
    } catch {
      // Unrecognized prompts keep the publication category label.
    }
  }
  if (reason === "cloud.fileProcessing" || reason === "files.processing") {
    try {
      const prompt = JSON.parse(waitPromptJson ?? "{}");
      const detail = fileProcessingFailureReason(prompt?.reason ?? prompt?.Reason, prompt?.message ?? prompt?.Message);
      if (
        [
          "ExtractionPlanUnknown",
          "ExtractionLayoutConflict",
          "PasswordUnknown",
          "StorageUnavailable",
          "DiskSpaceLimit",
          "FILE_DELIVERY_ARCHIVE_REVIEW_REQUIRED",
          "FILE_DELIVERY_RESULT_UNCERTAIN",
        ].includes(detail ?? "") || fileProcessingRecoveryReasons.has(detail ?? "")
      )
        reason = detail;
      const storageCode =
        localStorageProblemCode(detail) ??
        (detail === "StorageUnavailable"
          ? localStorageProblemCode(prompt?.message ?? prompt?.Message)
          : undefined);
      if (storageCode?.startsWith("LOCAL_STORAGE_")) reason = storageCode;
    } catch {
      // Old or malformed prompts still have a neutral category label.
    }
  }
  if (reason) {
    const key = `workflow.waitReason.${reason}`;
    const label = t<string>(key, { defaultValue: "" });
    if (label && label !== key) return label;
  }
  return t<string>("workflow.resume.guide.unknownReason");
}
