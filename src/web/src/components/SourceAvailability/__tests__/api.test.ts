import { beforeEach, describe, expect, it, vi } from "vitest";
import { sourceAvailabilityApi } from "../api";
const api = vi.hoisted(() => ({
  options: vi.fn(),
  save: vi.fn(),
  preview: vi.fn(),
  batch: vi.fn(),
  apply: vi.fn(),
  get: vi.fn(),
}));
vi.mock("@/sdk/BApi", () => ({
  default: {
    acquisition: { getAcquisitionOptions: api.options, putAcquisitionOptions: api.save },
    sourceAvailability: {
      previewAcquisitionSourceAvailability: api.preview,
      getSourceAvailabilityBatch: api.batch,
      applyAcquisitionSourceAvailability: api.apply,
      getAcquisitionSourceAvailability: api.get,
    },
  },
}));
const row = {
  key: "k",
  taskId: 3,
  title: "Example",
  partId: "p",
  sourceId: "s",
  url: "https://example.com",
  accessCode: "abc",
  providerId: "http",
  availability: {
    status: "confirmedUnavailable",
    reasonCode: "SOURCE_NOT_FOUND",
    probeId: "provider",
    checkedAt: "2026-01-01T00:00:00Z",
    expiresAt: "2026-01-01T06:00:00Z",
    evidence: { scope: "anonymous" },
  },
  disabled: false,
  protected: true,
  canDisable: false,
  canRestore: false,
};
beforeEach(() => {
  vi.clearAllMocks();
});
describe("source-availability SDK adapter", () => {
  it("preserves provider evidence and server protections", async () => {
    api.preview.mockResolvedValue({
      data: {
        batchId: "batch",
        status: "ready",
        processed: 1,
        total: 1,
        expiresAt: "later",
        rows: [row],
      },
    });
    const result = await sourceAvailabilityApi.preview([3], false);
    expect(api.preview).toHaveBeenCalledExactlyOnceWith(
      { taskIds: [3], force: false },
      { sensitiveResponse: true, showErrorToast: false },
    );
    expect(result.rows[0]).toMatchObject({
      taskTitle: "Example",
      url: row.url,
      accessCode: "abc",
      inUse: true,
      actionable: false,
      health: {
        status: "confirmedUnavailable",
        code: "SOURCE_NOT_FOUND",
        evidence: { scope: "anonymous" },
      },
    });
  });
  it("reads cached health without submitting probes or downloads", async () => {
    api.get.mockResolvedValue({ data: [row] });
    await sourceAvailabilityApi.acquisition(3);
    expect(api.get).toHaveBeenCalledWith(3, { sensitiveResponse: true, showErrorToast: false });
    expect(api.preview).not.toHaveBeenCalled();
    expect(api.apply).not.toHaveBeenCalled();
  });
  it("uses restore eligibility separately from disable eligibility", async () => {
    api.get.mockResolvedValue({
      data: [{ ...row, disabled: true, protected: false, canRestore: true, canDisable: false }],
    });
    expect((await sourceAvailabilityApi.acquisition(3))[0]).toMatchObject({
      disabled: true,
      actionable: true,
    });
  });
  it("keeps skipped mutations and their explanations", async () => {
    api.apply.mockResolvedValue({
      data: { appliedKeys: ["a"], skipped: [{ key: "b", reason: "SOURCE_CHANGED" }] },
    });
    const result = await sourceAvailabilityApi.apply("batch", ["a", "b"], "disable");
    expect(result).toEqual({
      changedCount: 1,
      skippedCount: 1,
      rows: [
        { key: "a", status: "applied" },
        { key: "b", status: "skipped", code: "SOURCE_CHANGED" },
      ],
    });
  });
  it("updates priority against current settings and preserves unrelated values", async () => {
    api.options.mockResolvedValue({
      data: {
        sourceProviderPriority: ["http"],
        concurrency: 7,
        libraryRootDirectory: "/original",
        autoPurchaseLimit: 10,
      },
    });
    api.save.mockResolvedValue({ code: 0 });
    await sourceAvailabilityApi.saveOptions({ providerPriority: ["baidu", "http"] });
    expect(api.save).toHaveBeenCalledExactlyOnceWith({
      sourceProviderPriority: ["baidu", "http"],
      concurrency: 7,
      libraryRootDirectory: "/original",
      autoPurchaseLimit: 10,
    });
  });
  it("does not fabricate successful data after an API rejection", async () => {
    api.preview.mockResolvedValue({ code: 400, message: "SOURCE_AVAILABILITY_SELECTION_INVALID" });
    await expect(sourceAvailabilityApi.preview([], false)).rejects.toThrow(
      "SOURCE_AVAILABILITY_SELECTION_INVALID",
    );
  });
});
