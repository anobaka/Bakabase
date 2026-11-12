import { beforeEach, expect, it, vi } from "vitest";
import { acquisitionContinuationApi as api } from "../components/acquisitionContinuationApi";
const rpc = vi.hoisted(() => ({ preview: vi.fn(), create: vi.fn(), findReceipt: vi.fn(), list: vi.fn(), get: vi.fn(), cancel: vi.fn() }));
vi.mock("@/sdk/BApi", () => ({ default: { acquisition: {
  previewAcquisitionContinuation: rpc.preview, createAcquisitionContinuation: rpc.create,
  getAcquisitionContinuationByPreview: rpc.findReceipt, getAcquisitionContinuations: rpc.list,
  getAcquisitionContinuation: rpc.get, cancelAcquisitionContinuation: rpc.cancel,
} } }));
beforeEach(() => { for (const call of Object.values(rpc)) call.mockReset().mockResolvedValue({ code: 0, data: {} }); });
const options = { sensitiveResponse: true, showErrorToast: false };

it("previews the fixed IDs and explicit scope in size order, with no space override", async () => {
  const controller = new AbortController();
  await api.preview([7, 2], "downloads", controller.signal);
  expect(rpc.preview).toHaveBeenCalledExactlyOnceWith({ taskIds: [7, 2], scope: "downloads", order: "sizeAsc" },
    { ...options, signal: controller.signal });
  expect(rpc.create).not.toHaveBeenCalled();
});
it("creates only from the fixed preview and resolves a lost reply using the receipt endpoint", async () => {
  rpc.create.mockRejectedValueOnce(new Error("connection lost"));
  await expect(api.create("token", [7])).rejects.toThrow("connection lost");
  await api.findReceipt("token");
  expect(rpc.create).toHaveBeenCalledExactlyOnceWith({ previewToken: "token", taskIds: [7] }, options);
  expect(rpc.findReceipt).toHaveBeenCalledExactlyOnceWith("token", options);
});
it("keeps history reads and cancellation separate from starting a continuation", async () => {
  await api.list(); await api.get("job"); await api.cancel("job");
  expect(rpc.list).toHaveBeenCalledExactlyOnceWith(options);
  expect(rpc.get).toHaveBeenCalledExactlyOnceWith("job", options);
  expect(rpc.cancel).toHaveBeenCalledExactlyOnceWith("job", options);
  expect(rpc.create).not.toHaveBeenCalled();
});
it("does not report rejected or empty continuation responses as success", async () => {
  rpc.create.mockResolvedValue({ code: 1, message: "CONTINUATION_REJECTED", data: null });
  await expect(api.create("token", [7])).rejects.toThrow("CONTINUATION_REJECTED");
  rpc.findReceipt.mockResolvedValue({ code: 0, data: null });
  await expect(api.findReceipt("token")).rejects.toThrow("CONTINUATION_REQUEST_FAILED");
});
it.each([true, false])("binds explicit reserve=%s to the queued continuation", async useEmergencySpaceReserve => {
  await api.create("token", [7], useEmergencySpaceReserve);
  expect(rpc.create).toHaveBeenCalledExactlyOnceWith(
    { previewToken: "token", taskIds: [7], useEmergencySpaceReserve }, options);
});
