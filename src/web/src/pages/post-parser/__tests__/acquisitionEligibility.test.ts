import type { PostParserTask } from "@/core/models/PostParserTask";
import { describe, expect, it } from "vitest";
import { PostParseTarget, PostParserSource } from "@/sdk/constants";
import {
  canAcquirePostResult,
  canCatchUpPostAcquisition,
  missingParsingWorkflowRunError,
} from "../acquisitionEligibility";

const complete = (): PostParserTask => ({
  id: 674,
  revision: 2,
  workflowRunId: null,
  source: PostParserSource.SoulPlus,
  link: "https://example.com/post",
  targets: [PostParseTarget.DownloadInfo],
  parsingState: "complete",
  results: {
    DownloadInfo: { isComplete: true, resources: [{ link: "https://example.com/file" }] },
  },
});

describe("saved post-result acquisition eligibility", () => {
  it("allows complete saved results without a producer run and the exact cleaned-history error", () => {
    const task = { ...complete(), error: missingParsingWorkflowRunError };
    expect(canAcquirePostResult(task)).toBe(true);
    expect(canCatchUpPostAcquisition(task)).toBe(true);
    expect(task.error).toBe(missingParsingWorkflowRunError);
  });

  it("does not generalize missing-history errors or incomplete extraction", () => {
    for (const error of ["Network failed", `${missingParsingWorkflowRunError} extra`]) {
      expect(canAcquirePostResult({ ...complete(), error })).toBe(false);
    }
    const task = complete();
    task.error = missingParsingWorkflowRunError;
    task.results = { DownloadInfo: { resources: [{ link: "https://example.com/file" }] } };
    expect(canAcquirePostResult(task)).toBe(false);
    expect(canCatchUpPostAcquisition({ ...complete(), parsingState: "partial" })).toBe(false);
  });

  it("keeps manual legacy-source selection while requiring a complete versioned result for catch-up", () => {
    const task = complete();
    task.results = { DownloadInfo: { resources: [{ link: "https://example.com/file" }] } };
    expect(canAcquirePostResult(task)).toBe(true);
    expect(canCatchUpPostAcquisition(task)).toBe(false);
    expect(canCatchUpPostAcquisition({ ...complete(), revision: undefined })).toBe(false);
  });

  it("does not waive current unpaid-content requirements or deleted tasks", () => {
    const task = complete();
    task.contentSnapshot = { locks: [{ blockId: "code", isBought: false }] };
    expect(canAcquirePostResult(task)).toBe(false);
    expect(canCatchUpPostAcquisition(task)).toBe(false);
    expect(canAcquirePostResult({ ...complete(), isDeleted: true })).toBe(false);
  });

  it("rejects saved target errors and malformed resources rather than trusting isComplete alone", () => {
    const task = { ...complete(), error: missingParsingWorkflowRunError };
    for (const value of [
      {
        data: { isComplete: true, resources: [{ link: "https://example.com/file" }] },
        error: "failed",
      },
      { isComplete: true, resources: [null, { link: "https://example.com/file" }] },
      { isComplete: true, resources: "not an array" },
    ]) {
      task.results = { DownloadInfo: value };
      expect(canAcquirePostResult(task)).toBe(false);
      expect(canCatchUpPostAcquisition(task)).toBe(false);
    }
  });

  it("keeps existing handoff ownership while leaving the manual plan entry available", () => {
    const task = complete();
    task.acquisitionHandoff = {
      id: "already-queued",
      status: "queued",
    } as NonNullable<PostParserTask["acquisitionHandoff"]>;
    expect(canAcquirePostResult(task)).toBe(true);
    expect(canCatchUpPostAcquisition(task)).toBe(false);
  });
});
