import { beforeEach, expect, it, vi } from "vitest";
import { acquisitionLocalBatchApi } from "../components/acquisitionLocalBatchApi";

const sdk = vi.hoisted(() => ({
  previewAcquisitionLocalRecoveryBatch: vi.fn(),
  previewAcquisitionLocalRecoveryBatchCapacity: vi.fn(),
  createAcquisitionLocalRecoveryBatch: vi.fn(),
  getAcquisitionLocalRecoveryBatches: vi.fn(),
  getAcquisitionLocalRecoveryBatch: vi.fn(),
  pauseAcquisitionLocalRecoveryBatch: vi.fn(),
  resumeAcquisitionLocalRecoveryBatch: vi.fn(),
  cancelAcquisitionLocalRecoveryBatch: vi.fn(),
}));
vi.mock("@/sdk/BApi", () => ({ default: { acquisition: sdk } }));
beforeEach(() => {
  for (const method of Object.values(sdk))
    method.mockReset().mockResolvedValue({ code: 0, data: {} });
});

it("uses automatic classification and rechecks capacity for only the fixed selected IDs", async () => {
  const signal = new AbortController().signal;
  await acquisitionLocalBatchApi.preview([3, 1], signal, "automatic");
  expect(sdk.previewAcquisitionLocalRecoveryBatch).toHaveBeenCalledExactlyOnceWith(
    { taskIds: [3, 1], mode: "automatic", order: "sizeAsc" },
    { sensitiveResponse: true, showErrorToast: false, signal },
  );
  await acquisitionLocalBatchApi.capacity("fixed", [1], signal);
  expect(sdk.previewAcquisitionLocalRecoveryBatchCapacity).toHaveBeenCalledExactlyOnceWith(
    { previewToken: "fixed", taskIds: [1] }, { sensitiveResponse: true, showErrorToast: false, signal },
  );
  expect(sdk.createAcquisitionLocalRecoveryBatch).not.toHaveBeenCalled();
});

it("keeps preview evidence private and passes an abort signal only to the read-only preview", async () => {
  const signal = new AbortController().signal;
  await acquisitionLocalBatchApi.preview([3, 1], signal);
  expect(sdk.previewAcquisitionLocalRecoveryBatch).toHaveBeenCalledWith(
    { taskIds: [3, 1], mode: "localOnly", order: "sizeAsc" },
    { sensitiveResponse: true, showErrorToast: false, signal },
  );
});

it.each([false, true])(
  "sends explicit reserve acknowledgement %s with the frozen token and task set",
  async (allowStorageOverride) => {
    await acquisitionLocalBatchApi.create("fixed-preview", [3, 1], allowStorageOverride);
    expect(sdk.createAcquisitionLocalRecoveryBatch).toHaveBeenCalledWith(
      { previewToken: "fixed-preview", taskIds: [3, 1], allowStorageOverride },
      { sensitiveResponse: true, showErrorToast: false },
    );
  },
);

it.each(["pause", "resume", "cancel"] as const)(
  "scopes %s to a single existing queue without changing task payloads",
  async (action) => {
    await acquisitionLocalBatchApi[action]("queue-id");
    const method = sdk[`${action}AcquisitionLocalRecoveryBatch`];
    expect(method).toHaveBeenCalledWith("queue-id", {
      sensitiveResponse: true,
      showErrorToast: false,
    });
  },
);

it("reads saved queues and rejects failed or absent responses", async () => {
  sdk.getAcquisitionLocalRecoveryBatches.mockResolvedValue({ code: 0, data: [] });
  expect(await acquisitionLocalBatchApi.list()).toEqual([]);
  sdk.getAcquisitionLocalRecoveryBatch.mockResolvedValue({
    code: 400,
    message: "LOCAL_BATCH_NOT_FOUND",
    data: null,
  });
  await expect(acquisitionLocalBatchApi.get("absent")).rejects.toThrow("LOCAL_BATCH_NOT_FOUND");
  sdk.createAcquisitionLocalRecoveryBatch.mockResolvedValue({ code: 0, data: null });
  await expect(acquisitionLocalBatchApi.create("token", [1], false)).rejects.toThrow(
    "LOCAL_BATCH_REQUEST_FAILED",
  );
});
