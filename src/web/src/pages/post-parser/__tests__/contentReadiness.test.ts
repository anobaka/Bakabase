import { describe, expect, it } from "vitest";
import type { PostContentSnapshot, PostParserTask } from "@/core/models/PostParserTask";
import { PostParserSource, PostParseTarget, WorkflowRunStatus } from "@/sdk/constants";
import { hasBlockingContentLocks, unresolvedContentLocks } from "../contentReadiness";
import { getTaskDisplayStatus } from "../taskStatus";

const content = (): PostContentSnapshot => ({
  locks: [
    { blockId: "proof", url: "same-target", isBought: false },
    { blockId: "credentials", url: "same-target", isBought: false },
  ],
  readiness: {
    basis: "analysis",
    fingerprint: "current-snapshot",
    policyVersion: "v1",
    decisions: [
      {
        blockId: "proof",
        relevance: "optional",
        purpose: "purchaseProof",
        evidence: ["Purchase proof image"],
      },
      {
        blockId: "credentials",
        relevance: "required",
        purpose: "downloadCredentials",
        evidence: ["Extraction code inside"],
      },
    ],
  },
});
const task = (snapshot: PostContentSnapshot): PostParserTask => ({
  id: 1,
  source: PostParserSource.SoulPlus,
  link: "https://example.com/post",
  targets: [PostParseTarget.DownloadInfo],
  contentSnapshot: snapshot,
  parsingState: "complete",
  workflowStatus: WorkflowRunStatus.Success,
});
describe("content readiness consumption", () => {
  it("uses block identity and valid relevance, preserving each unpaid fact even when targets share a URL", () => {
    const snapshot = content();
    expect(unresolvedContentLocks(snapshot).map((lock) => lock.blockId)).toEqual(["credentials"]);
    expect(snapshot.locks.every((lock) => !lock.isBought)).toBe(true);
    expect(hasBlockingContentLocks(task(snapshot))).toBe(true);
  });
  it("does not infer optional content from donation or purchase-proof purpose alone", () => {
    const snapshot = content();
    snapshot.readiness!.decisions[0].relevance = "unknown";
    snapshot.readiness!.decisions[0].purpose = "donation";
    expect(unresolvedContentLocks(snapshot)).toHaveLength(2);
    snapshot.readiness = null;
    expect(unresolvedContentLocks(snapshot)).toHaveLength(2);
  });
  it("does not substitute a purchase URL for a missing block ID or accept conflicting relevance", () => {
    const snapshot = content();
    snapshot.locks[0].blockId = null;
    expect(unresolvedContentLocks(snapshot)).toHaveLength(2);
    snapshot.locks[0].blockId = "proof";
    snapshot.readiness!.decisions.push({
      blockId: "proof",
      relevance: "required",
      purpose: "downloadCredentials",
      evidence: [],
    });
    expect(unresolvedContentLocks(snapshot)).toHaveLength(2);
  });
  it("accepts complete extraction with a current optional-content assessment without pretending it was purchased", () => {
    const record = task(content());
    record.contentSnapshot!.readiness!.decisions[1].relevance = "optional";
    record.results = {
      DownloadInfo: { isComplete: true, resources: [{ link: "https://example.com/file" }] },
    };
    expect(hasBlockingContentLocks(record)).toBe(false);
    expect(getTaskDisplayStatus(record).group).toBe("success");
    expect(record.contentSnapshot!.locks.every((lock) => !lock.isBought)).toBe(true);
  });
  it("restores current lock requirements after revocation while preserving historical extraction results", () => {
    const record = task(content());
    const oldAssessment = record.contentSnapshot!.readiness!;
    const oldResult = {
      isComplete: true,
      contentReadiness: oldAssessment,
      resources: [{ link: "https://example.com/file" }],
    };
    record.results = { DownloadInfo: oldResult };
    record.contentSnapshot!.readiness = null;
    expect(hasBlockingContentLocks(record)).toBe(true);
    expect(getTaskDisplayStatus(record).group).toBe("attention");
    expect(oldResult.isComplete).toBe(true);
    expect(record.contentSnapshot!.locks.every((lock) => !lock.isBought)).toBe(true);
  });
  it("does not call incomplete extraction complete merely because remaining content is optional", () => {
    const snapshot = content();
    snapshot.readiness!.decisions[1].relevance = "optional";
    const record = task(snapshot);
    record.results = { DownloadInfo: { isComplete: false, resources: [] } };
    expect(hasBlockingContentLocks(record)).toBe(false);
    expect(getTaskDisplayStatus(record).group).toBe("attention");
  });
});
