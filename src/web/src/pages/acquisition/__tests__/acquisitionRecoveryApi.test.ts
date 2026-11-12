import { beforeEach, expect, it, vi } from "vitest";
import { acquisitionRecoveryApi as api } from "../components/acquisitionRecoveryApi";
const mocks = vi.hoisted(() => ({ preview: vi.fn(), apply: vi.fn(), cancel: vi.fn() }));
vi.mock("@/sdk/BApi", () => ({
  default: {
    acquisition: {
      previewAcquisitionTaskRecovery: mocks.preview,
      applyAcquisitionTaskRecovery: mocks.apply,
      cancelAcquisitionTaskRecoveryDiagnosis: mocks.cancel,
    },
  },
}));
beforeEach(() => {
  vi.clearAllMocks();
  mocks.preview.mockResolvedValue({ code: 0, data: { rows: [] } });
  mocks.apply.mockResolvedValue({ code: 0, data: { continuedTaskIds: [], skipped: [] } });
  mocks.cancel.mockImplementation(async diagnosisId => ({ code: 0, data: { diagnosisId, status: "stopped" } }));
});
it("retains workflow behavior for existing callers and explicitly requests downloads when selected", async () => {
  await api.preview([1]);
  await api.preview([2], "downloads");
  await api.preview([3], "localFiles");
  expect(mocks.preview).toHaveBeenNthCalledWith(
    1,
    { taskIds: [1], scope: "workflow", diagnosisId: undefined },
    expect.any(Object),
  );
  expect(mocks.preview).toHaveBeenNthCalledWith(
    2,
    { taskIds: [2], scope: "downloads", diagnosisId: undefined },
    expect.any(Object),
  );
  expect(mocks.preview).toHaveBeenNthCalledWith(
    3,
    { taskIds: [3], scope: "localFiles", diagnosisId: undefined },
    expect.any(Object),
  );
});
it("binds a cancellable diagnosis to its ID and forwards its AbortSignal without adding execution options", async () => {
  const controller = new AbortController();
  await api.preview([7, 9], "localFiles", { diagnosisId: "diagnosis", signal: controller.signal });
  expect(mocks.preview).toHaveBeenCalledExactlyOnceWith({ taskIds: [7, 9], scope: "localFiles", diagnosisId: "diagnosis" },
    { sensitiveResponse: true, showErrorToast: false, signal: controller.signal });
  await api.cancelDiagnosis("diagnosis");
  expect(mocks.cancel).toHaveBeenCalledExactlyOnceWith("diagnosis", { sensitiveResponse: true, showErrorToast: false });
  expect(mocks.apply).not.toHaveBeenCalled();
});
it("applies only the immutable server preview token and chosen tasks, never switching scope in apply", async () => {
  await api.apply("downloads-bound-token", [2]);
  expect(mocks.apply).toHaveBeenCalledExactlyOnceWith(
    { previewToken: "downloads-bound-token", taskIds: [2] },
    { sensitiveResponse: true, showErrorToast: false },
  );
});
it("does not report rejected recovery requests as success", async () => {
  mocks.apply.mockResolvedValue({ code: 1, message: "RECOVERY_OUTCOME_UNKNOWN", data: null });
  await expect(api.apply("token", [2])).rejects.toThrow("RECOVERY_OUTCOME_UNKNOWN");
});
it.each([true, false])("keeps explicit reserve=%s instead of dropping false", async useEmergencySpaceReserve => {
  await api.apply("downloads-bound-token", [2], useEmergencySpaceReserve);
  expect(mocks.apply).toHaveBeenCalledExactlyOnceWith(
    { previewToken: "downloads-bound-token", taskIds: [2], useEmergencySpaceReserve }, expect.any(Object));
});
