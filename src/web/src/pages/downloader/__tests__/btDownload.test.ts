import { beforeEach, describe, expect, it, vi } from "vitest";
import { downloadTaskWithBt } from "../btDownload";
import { DownloadTaskActionOnConflict } from "@/sdk/constants";
const { btDownloadTask } = vi.hoisted(() => ({ btDownloadTask: vi.fn() }));
vi.mock("@/sdk/BApi", () => ({ default: { downloadTask: { btDownloadTask } } }));
beforeEach(() => {
  btDownloadTask.mockReset();
  btDownloadTask.mockResolvedValue({ code: 0 });
});
describe("atomic BT acquisition switch", () => {
  it.each([
    DownloadTaskActionOnConflict.NotSet,
    DownloadTaskActionOnConflict.StopOthers,
    DownloadTaskActionOnConflict.Ignore,
  ])(
    "keeps the original task and conflict policy %s in one server action",
    async (actionOnConflict) => {
      await downloadTaskWithBt(17, actionOnConflict);
      expect(btDownloadTask).toHaveBeenCalledExactlyOnceWith(
        17,
        { actionOnConflict },
        { showErrorToast: false },
      );
    },
  );
});
