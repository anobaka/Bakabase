import { beforeEach, describe, expect, it, vi } from "vitest";
import { downloadTaskTorrentFile } from "../torrentFileDownload";
import { DownloadTaskActionOnConflict } from "@/sdk/constants";

const { torrentFileDownloadTask } = vi.hoisted(() => ({ torrentFileDownloadTask: vi.fn() }));
vi.mock("@/sdk/BApi", () => ({ default: { downloadTask: { torrentFileDownloadTask } } }));
beforeEach(() => {
  torrentFileDownloadTask.mockReset();
  torrentFileDownloadTask.mockResolvedValue({ code: 0 });
});

describe("atomic torrent-file acquisition switch", () => {
  it.each([
    DownloadTaskActionOnConflict.NotSet,
    DownloadTaskActionOnConflict.StopOthers,
    DownloadTaskActionOnConflict.Ignore,
  ])("retains the task and conflict policy %s in one server action", async (actionOnConflict) => {
    await downloadTaskTorrentFile(17, actionOnConflict);
    expect(torrentFileDownloadTask).toHaveBeenCalledExactlyOnceWith(
      17,
      { actionOnConflict },
      { showErrorToast: false },
    );
  });
});
