import { beforeEach, expect, it, vi } from "vitest";
import { acquisitionDeletionApi as api } from "../components/acquisitionDeletionApi";

const mocks = vi.hoisted(() => ({ preview: vi.fn(), remove: vi.fn(), progress: vi.fn(), names: vi.fn() }));
vi.mock("@/sdk/BApi", () => ({
  default: { acquisition: {
    previewDeleteAcquisitionTasks: mocks.preview,
    deleteAcquisitionTasks: mocks.remove,
    getAcquisitionTaskDeletionProgress: mocks.progress,
    getAcquisitionTaskNames: mocks.names,
  } },
}));
beforeEach(() => {
  vi.clearAllMocks();
  mocks.remove.mockResolvedValue({ code: 0, data: { deletedTaskIds: [], skipped: [] } });
  mocks.progress.mockResolvedValue({ code: 0, data: { previewId: "fixed" } });
  mocks.names.mockResolvedValue({ code: 0, data: { 2: null, 5: "Known task" } });
});

it("retrieves only task names without file previews", async () => {
  const controller = new AbortController();
  await expect(api.names([2, 5], controller.signal)).resolves.toEqual({ 5: "Known task" });
  expect(mocks.names).toHaveBeenCalledExactlyOnceWith({ taskIds: [2, 5] }, {
    sensitiveResponse: true, showErrorToast: false, signal: controller.signal,
  });
  expect(mocks.preview).not.toHaveBeenCalled();
});

it("submits fixed IDs and current options directly without preparing a preview", async () => {
  await api.remove("operation", [2, 5], { cleanupFiles: true, deleteDeliveredFiles: true, removeTaskIfCleanupFails: true });
  expect(mocks.remove).toHaveBeenCalledExactlyOnceWith({
    previewId: "operation", taskIds: [2, 5], cleanupFiles: true, deleteDeliveredFiles: true, removeTaskIfCleanupFails: true,
  }, { sensitiveResponse: true, showErrorToast: false });
  expect(mocks.preview).not.toHaveBeenCalled();
});

it("uses a cancellable read for progress with the submitted operation ID", async () => {
  const controller = new AbortController();
  await api.progress("fixed", controller.signal);
  expect(mocks.progress).toHaveBeenCalledExactlyOnceWith("fixed", {
    sensitiveResponse: true, showErrorToast: false, signal: controller.signal,
  });
});

it("propagates failed requests without automatically retrying deletion", async () => {
  mocks.remove.mockResolvedValueOnce({ code: 1, message: "DELETION_OUTCOME_UNKNOWN", data: null });
  await expect(api.remove("fixed", [1])).rejects.toThrow("DELETION_OUTCOME_UNKNOWN");
  expect(mocks.remove).toHaveBeenCalledTimes(1);
});

it("binds historical cleanup to the reviewed scopes and submits the server preview without new task IDs", async () => {
  const signal = new AbortController().signal;
  const scope = { cleanupFiles: true, deleteDeliveredFiles: false, retryRetainedFiles: true };
  mocks.preview.mockResolvedValue({ code: 0, data: { previewId: "review" } });
  await api.previewRetained([7, 9], scope, signal);
  expect(mocks.preview).toHaveBeenCalledWith({ taskIds: [7, 9], ...scope },
    { sensitiveResponse: true, showErrorToast: false, signal });
  await api.remove("review", undefined, scope);
  expect(mocks.remove).toHaveBeenCalledExactlyOnceWith({ previewId: "review", ...scope },
    { sensitiveResponse: true, showErrorToast: false });
});
