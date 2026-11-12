import { beforeEach, describe, expect, it, vi } from "vitest";
import { postAcquireInput, postSourceRef } from "../acquireAdapter";
import { PostParseTarget, PostParserSource } from "@/sdk/constants";
import type { PostParserTask } from "@/core/models/PostParserTask";
import BApi from "@/sdk/BApi";

vi.mock("@/sdk/BApi", () => ({
  default: { postParser: { createPostAcquisitionPlan: vi.fn() } },
}));
const task: PostParserTask = {
  id: 41,
  revision: 8,
  source: PostParserSource.SoulPlus,
  link: "https://example.com/post/41",
  targets: [PostParseTarget.DownloadInfo],
  results: {
    [PostParseTarget.DownloadInfo]: {
      resources: [
        { link: "https://gofile.io/d/duplicate", password: "secret" },
        { link: "https://gofile.io/d/duplicate", password: "secret" },
      ],
    },
  },
};
beforeEach(() =>
  vi
    .mocked(BApi.postParser.createPostAcquisitionPlan)
    .mockReset()
    .mockResolvedValue({
      code: 0,
      data: { mode: "files", runs: [], resourceIds: [], acquisitionTaskIds: [] },
    }),
);
it("keeps source-specific validation at the page boundary and submits original indices", async () => {
  const { candidates, adapter } = postAcquireInput(task);
  expect(candidates).toHaveLength(1);
  expect(candidates[0]).toMatchObject({ id: "0", archivePassword: "secret" });
  await adapter.submit({ mode: "files", candidates, selectedCandidateIds: ["0"] }, "/downloads");
  expect(BApi.postParser.createPostAcquisitionPlan).toHaveBeenCalledWith(
    expect.objectContaining({ taskId: 41, revision: 8, resourceIndices: [0] }),
    { outputDirectory: "/downloads" },
  );
  expect(vi.mocked(BApi.postParser.createPostAcquisitionPlan).mock.calls[0][0]).not.toHaveProperty(
    "candidates",
  );
});
it("uses the same generic source identity for history, with revision optional", () => {
  expect(postSourceRef(task)).toEqual({ module: "postParser", entityKind: "task", entityId: "41" });
  expect(postSourceRef(task, true).revision).toBe("8");
});

it("excludes legacy inline images without renumbering download identities", async () => {
  const saved: PostParserTask = {
    ...task,
    downloadMedia: {
      excludedResourceIndices: [0, 2],
      coverCandidates: [{ url: "https://example.com/screenshot.jpg", kind: "preview" }],
    },
    results: {
      [PostParseTarget.DownloadInfo]: {
        resources: [
          { link: "https://example.com/screenshot.jpg" },
          { link: "https://gofile.io/d/download" },
          { link: "https://example.com/other.jpg" },
          {
            link: "https://example.com/disguised-archive.jpg",
            extraction: { requirement: "required", steps: [] },
          },
        ],
      },
    },
  };
  const snapshot = JSON.stringify(saved);
  const { candidates, adapter } = postAcquireInput(saved);
  expect(candidates.map((candidate) => candidate.id)).toEqual(["1", "3"]);
  await adapter.submit(
    { mode: "files", candidates, selectedCandidateIds: ["1", "3"] },
    "/downloads",
  );
  expect(BApi.postParser.createPostAcquisitionPlan).toHaveBeenCalledWith(
    expect.objectContaining({ resourceIndices: [1, 3] }),
    { outputDirectory: "/downloads" },
  );
  expect(JSON.stringify(saved)).toBe(snapshot);
});

it("preserves per-candidate method and account choices at the trusted post boundary", async () => {
  const { candidates, adapter } = postAcquireInput(task);
  await adapter.submit(
    {
      mode: "files",
      candidates: candidates.map((candidate) => ({
        ...candidate,
        methodId: "pixeldrain.rclone",
        accountId: "account-1",
      })),
      selectedCandidateIds: ["0"],
    },
    "/downloads",
  );
  expect(BApi.postParser.createPostAcquisitionPlan).toHaveBeenCalledWith(
    expect.objectContaining({
      candidateMethods: { "0": "pixeldrain.rclone" },
      candidateAccounts: { "0": "account-1" },
    }),
    { outputDirectory: "/downloads" },
  );
});

it("sends only explicitly changed processing plans and never changes the saved parsing result", async () => {
  const { candidates, adapter } = postAcquireInput(task);
  const snapshot = JSON.stringify(task.results);
  await adapter.submit({ mode: "files", candidates, selectedCandidateIds: ["0"] }, "/downloads");
  expect(vi.mocked(BApi.postParser.createPostAcquisitionPlan).mock.calls[0][0]).toHaveProperty(
    "candidateProcessingPlans",
    {},
  );
  const plan = JSON.stringify({ requirement: "notRequired", steps: [] });
  await adapter.submit(
    {
      mode: "files",
      candidates: candidates.map((candidate) => ({ ...candidate, extractionPlanJson: plan })),
      selectedCandidateIds: ["0"],
    },
    "/downloads",
  );
  expect(vi.mocked(BApi.postParser.createPostAcquisitionPlan).mock.calls[1][0]).toMatchObject({
    candidateProcessingPlans: { "0": plan },
  });
  expect(JSON.stringify(task.results)).toBe(snapshot);
});
