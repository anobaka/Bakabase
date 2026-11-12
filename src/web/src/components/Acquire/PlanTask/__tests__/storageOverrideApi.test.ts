import { expect, it, vi } from "vitest";
import { storageOverrideApi } from "../storageOverrideApi";
const api = vi.hoisted(() => ({ preview: vi.fn(), apply: vi.fn(), capacity: vi.fn() }));
vi.mock("@/sdk/BApi", () => ({
  default: {
    acquisition: {
      previewAcquisitionStorageOverride: api.preview,
      previewAcquisitionLocalRecoveryBatch: api.capacity,
      applyAcquisitionStorageOverride: api.apply,
    },
  },
}));

it("uses a non-queueing capacity read and discards measurements from a different run", async () => {
  const signal = new AbortController().signal;
  const data = { rows: [{taskId: 42, workflowRunId: 77}], volumes: [{availableBytes: 100}] };
  api.capacity.mockResolvedValue({code: 0, data});
  expect(await storageOverrideApi.capacity(42, 77, signal)).toEqual(data.volumes);
  expect(api.capacity).toHaveBeenCalledWith({taskIds:[42], mode:"capacityOnly", order:"sizeAsc"},
    expect.objectContaining({signal, sensitiveResponse:true}));
  expect(await storageOverrideApi.capacity(42, 78, signal)).toEqual([]);
});

it.each(["configured", "emergency"] as const)("binds %s to the reviewed token on a single task", async (reserveMode) => {
  vi.clearAllMocks();
  api.preview.mockResolvedValue({ code: 0, data: { token: "reviewed" } });
  api.apply.mockResolvedValue({ code: 0, data: { status: "applied" } });
  expect(await storageOverrideApi.preview(42)).toEqual({ token: "reviewed" });
  expect(await storageOverrideApi.apply(42, "reviewed", reserveMode)).toEqual({ status: "applied" });
  expect(api.preview).toHaveBeenCalledExactlyOnceWith(
    42,
    undefined,
    expect.objectContaining({ sensitiveResponse: true }),
  );
  expect(api.apply).toHaveBeenCalledExactlyOnceWith(
    42,
    { token: "reviewed", confirmed: true, reserveMode },
    expect.objectContaining({ sensitiveResponse: true }),
  );
});

it.each([true, false])("preserves the node answer and explicit download reserve=%s", async selected => {
  vi.clearAllMocks();
  api.preview.mockResolvedValue({ code: 0, data: { token: "download-token", scope: "download" } });
  api.apply.mockResolvedValue({ code: 0, data: { status: "applied" } });
  await storageOverrideApi.preview(42, "download");
  const signal = '{"action":"retry","targetId":"part","parameters":{"keep":"unchanged"}}';
  await storageOverrideApi.resumeDownload(42, "download-token", signal, selected);
  expect(api.preview).toHaveBeenCalledExactlyOnceWith(42, { scope: "download" }, expect.any(Object));
  expect(api.apply).toHaveBeenCalledExactlyOnceWith(42,
    { token: "download-token", confirmed: true, signalJson: signal, useEmergencySpaceReserve: selected },
    expect.objectContaining({ sensitiveResponse: true }));
});

it("rejects a server refusal even when a response contains data", async () => {
  api.apply.mockResolvedValue({ code: 400, message: "ACQUISITION_TASK_CHANGED", data: {} });
  await expect(storageOverrideApi.apply(42, "stale", "emergency")).rejects.toThrow("ACQUISITION_TASK_CHANGED");
});
