import { describe, expect, it } from "vitest";
import {
  archiveDiscoveryReasons,
  archiveCompatibilityReasons,
  canRetryFileProcessing,
  fileProcessingFailureReason,
} from "../fileProcessingFailure";

describe("legacy archive discovery recovery", () => {
  const legacy =
    "No supported archive was recognized inside the selected media-prefixed file. Revise the extraction step.";
  it("offers a detector retry only for the exact saved legacy diagnostic", () => {
    const reason = fileProcessingFailureReason("ExtractionPlanUnknown", legacy);
    expect(reason).toBe("ArchiveProbeUpgradeRequired");
    expect(archiveDiscoveryReasons.has(reason!)).toBe(true);
    expect(canRetryFileProcessing(reason)).toBe(true);
    expect(fileProcessingFailureReason("ExtractionPlanUnknown", legacy + " extra context")).toBe(
      "ExtractionPlanUnknown",
    );
    expect(fileProcessingFailureReason("ArchiveProbeIncomplete", legacy)).toBe(
      "ArchiveProbeIncomplete",
    );
    expect(canRetryFileProcessing("ArchiveProbeIncomplete")).toBe(false);
  });
});

describe("ZIP compatibility recovery", () => {
  it("only retries missing tools and storage, never corruption or unsupported encodings", () => {
    for (const reason of archiveCompatibilityReasons)
      expect(canRetryFileProcessing(reason)).toBe(["DEPENDENCY_REQUIRED", "ArchiveStorageUnavailable"].includes(reason));
  });
});
