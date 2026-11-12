import type { TFunction } from "i18next";
import { describe, expect, it } from "vitest";
import { fileCleanupCoverageIncomplete, workflowWaitReasonLabel } from "../waitReasonLabel";
import cn from "@/locales/cn/pages/cloudDownload.json";
import workflow from "@/locales/cn/pages/workflow.json";
import recovery from "@/locales/cn/pages/workflowFileRecovery.json";
import { archiveCompatibilityReasons } from "../fileProcessingFailure";

const labels: Record<string, string> = { ...cn, ...workflow, ...recovery };
const t = ((key: string, options?: { defaultValue?: string }) =>
  labels[key] ?? options?.defaultValue ?? key) as TFunction;

describe("file-processing wait reason", () => {
  it.each([...archiveCompatibilityReasons])("shows a localized compatibility cause for %s without private diagnostics", (reason) => {
    const value = workflowWaitReasonLabel(t, "files.processing", JSON.stringify({ reason, message: "private-password-or-path" }));
    expect(value).toBe(labels[`workflow.waitReason.${reason}`]);
    expect(value).not.toContain("private");
  });
  it.each([
    { message: "FILE_CLEANUP_COVERAGE_INCOMPLETE: license.key was not accounted for" },
    { Message: "FILE_CLEANUP_COVERAGE_INCOMPLETE" },
  ])("localizes cleanup coverage without exposing file details: %j", (prompt) => {
    const label = workflowWaitReasonLabel(t, "artifacts.cleanup", JSON.stringify(prompt));
    expect(label).toBe(labels["workflow.waitReason.FILE_CLEANUP_COVERAGE_INCOMPLETE"]);
    expect(label).not.toContain("license.key");
  });

  it.each([null, "bad json", "null", '{"message":42}',
    '{"message":"FILE_CLEANUP_COVERAGE_INCOMPLETE_OTHER"}',
    '{"reason":"FILE_CLEANUP_COVERAGE_INCOMPLETE"}',
    '{"message":"Unrecognized private diagnostic"}',
  ])("keeps unrecognized cleanup prompts neutral: %s", (prompt) => {
    expect(fileCleanupCoverageIncomplete(prompt)).toBe(false);
    expect(workflowWaitReasonLabel(t, "artifacts.cleanup", prompt))
      .toBe(labels["workflow.waitReason.artifacts.cleanup"]);
  });

  it("does not interpret a cleanup diagnostic as another activity's wait", () => {
    expect(workflowWaitReasonLabel(t, "cloud.fileProcessing", JSON.stringify({
      message: "FILE_CLEANUP_COVERAGE_INCOMPLETE: private details",
    }))).toBe("等待确认文件处理事项");
  });

  it("recognizes the exact historical timeout without asking for missing instructions", () => {
    expect(workflowWaitReasonLabel(t, "cloud.fileProcessing", JSON.stringify({
      reason: "ExtractionPlanUnknown",
      message: "Archive format discovery did not finish within its time budget. Narrow the selector or revise the plan.",
    }))).toBe("压缩格式识别超时");
  });
  it.each(["ArchiveProbeTimedOut", "ArchiveDetectionBudgetExceeded", "ArchiveProbeOutputLimit", "ArchiveProbeToolFailed", "ArchiveProbeIncomplete", "ProcessingInputNotMatched", "ArchiveInputRequired", "ProcessingPathConflict"])(
    "shows typed cause %s", (reason) => {
      expect(workflowWaitReasonLabel(t, "files.processing", JSON.stringify({ reason })))
        .toBe(labels[`workflow.waitReason.${reason}`]);
    },
  );
  it.each([
    "LOCAL_STORAGE_FULL",
  "LOCAL_STORAGE_RESERVE_LOW",
    "LOCAL_STORAGE_WRITE_FAILED",
    "LOCAL_STORAGE_ACCESS_DENIED",
    "LOCAL_STORAGE_UNAVAILABLE",
    "DiskSpaceLimit",
  ])(
    "shows the actionable local storage reason %s without requiring a password or new recipe",
    (reason) => {
      expect(workflowWaitReasonLabel(t, "cloud.fileProcessing", JSON.stringify({ reason }))).toBe(
        labels[`workflow.waitReason.${reason}`],
      );
    },
  );
  it("uses a typed storage cause from a storage suspension message", () => {
    expect(
      workflowWaitReasonLabel(
        t,
        "files.processing",
        JSON.stringify({ Reason: "StorageUnavailable", Message: "LOCAL_STORAGE_ACCESS_DENIED" }),
      ),
    ).toBe(labels["workflow.waitReason.LOCAL_STORAGE_ACCESS_DENIED"]);
  });
  it("shows the reserve guard for publication instead of suggesting a destination conflict", () => {
    expect(workflowWaitReasonLabel(t, "artifacts.publication", JSON.stringify({
      reason: "StorageUnavailable", message: "LOCAL_STORAGE_RESERVE_LOW",
    }))).toBe(labels["workflow.waitReason.LOCAL_STORAGE_RESERVE_LOW"]);
  });
  it("identifies disconnected processing storage without requesting a new plan", () => {
    expect(
      workflowWaitReasonLabel(t, "cloud.fileProcessing", '{"reason":"StorageUnavailable"}'),
    ).toBe("等待恢复存储连接");
  });
  it("does not ask for a password when only the processing plan is missing", () => {
    const label = workflowWaitReasonLabel(
      t,
      "cloud.fileProcessing",
      JSON.stringify({
        reason: "ExtractionPlanUnknown",
        password: "already-present",
      }),
    );
    expect(label).toBe("等待补充文件处理方案");
    expect(label).not.toContain("密码");
  });

  it("asks for a password only when the execution reports it missing", () => {
    expect(workflowWaitReasonLabel(t, "cloud.fileProcessing", '{"reason":"PasswordUnknown"}')).toBe(
      "需要提供解压密码",
    );
    expect(workflowWaitReasonLabel(t, "files.processing", '{"Reason":"PasswordUnknown"}')).toBe(
      "需要提供解压密码",
    );
  });

  it.each([null, "bad json", "null", '{"reason":"PRIVATE_DIAGNOSTIC"}'])(
    "keeps unknown historical prompts neutral and does not expose their data: %s",
    (prompt) => {
      expect(workflowWaitReasonLabel(t, "cloud.fileProcessing", prompt)).toBe(
        "等待确认文件处理事项",
      );
    },
  );

  it("does not reinterpret another activity's prompt", () => {
    expect(workflowWaitReasonLabel(t, "cloud.chooseSources", '{"reason":"PasswordUnknown"}')).toBe(
      "请选择下载内容和镜像",
    );
  });
});


it("labels uncertain post-extraction results without claiming an archive is the final deliverable", () => {
  expect(workflowWaitReasonLabel(t, "cloud.fileProcessing", JSON.stringify({reason: "FILE_DELIVERY_RESULT_UNCERTAIN"})))
    .toBe(labels["workflow.waitReason.FILE_DELIVERY_RESULT_UNCERTAIN"]);
});
