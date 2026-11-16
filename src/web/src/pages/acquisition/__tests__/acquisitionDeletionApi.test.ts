import { beforeEach, expect, it, vi } from "vitest";
import { acquisitionDeletionApi as api } from "../components/acquisitionDeletionApi";

const mocks = vi.hoisted(() => ({ preview: vi.fn(), remove: vi.fn(), progress: vi.fn() }));
vi.mock("@/sdk/BApi", () => ({
  default: {
    acquisition: {
      previewDeleteAcquisitionTasks: mocks.preview,
      deleteAcquisitionTasks: mocks.remove,
      getAcquisitionTaskDeletionProgress: mocks.progress,
    },
  },
}));
beforeEach(() => {
  vi.clearAllMocks();
  mocks.preview.mockResolvedValue({ code: 0, data: { rows: [] } });
  mocks.remove.mockResolvedValue({ code: 0, data: { deletedTaskIds: [], skipped: [] } });
  mocks.progress.mockResolvedValue({ code: 0, data: { previewId: "fixed" } });
});

it("forwards preview cancellation with the fixed IDs and file scopes", async () => {
  const controller = new AbortController();
  await api.preview([2, 5], { cleanupFiles: true, deleteDeliveredFiles: true }, controller.signal);
  expect(mocks.preview).toHaveBeenCalledExactlyOnceWith(
    { taskIds: [2, 5], cleanupFiles: true, deleteDeliveredFiles: true },
    { sensitiveResponse: true, showErrorToast: false, signal: controller.signal },
  );
  expect(mocks.remove).not.toHaveBeenCalled();
});

it("uses a cancellable read for progress, while deletion remains a single token-bound request", async () => {
  const controller = new AbortController();
  await api.progress("fixed", controller.signal);
  expect(mocks.progress).toHaveBeenCalledExactlyOnceWith("fixed", {
    sensitiveResponse: true,
    showErrorToast: false,
    signal: controller.signal,
  });
  const choices = {
    removeTaskIfCleanupFails: true,
  };
  await api.remove("fixed", choices);
  expect(mocks.remove).toHaveBeenCalledExactlyOnceWith(
    { previewId: "fixed", ...choices },
    { sensitiveResponse: true, showErrorToast: false },
  );
});

it("propagates failed requests without retrying deletion", async () => {
  mocks.remove.mockResolvedValueOnce({ code: 1, message: "DELETION_OUTCOME_UNKNOWN", data: null });
  await expect(api.remove("fixed")).rejects.toThrow("DELETION_OUTCOME_UNKNOWN");
  expect(mocks.remove).toHaveBeenCalledTimes(1);
});
