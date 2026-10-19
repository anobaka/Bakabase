import BApi from "@/sdk/BApi";
import { DownloadTaskActionOnConflict } from "@/sdk/constants";

/** Save only the torrent through one server action, preserving the task and its directory. */
export const downloadTaskTorrentFile = (
  id: number,
  actionOnConflict = DownloadTaskActionOnConflict.NotSet,
) => BApi.downloadTask.torrentFileDownloadTask(id, { actionOnConflict }, { showErrorToast: false });
