import BApi from "@/sdk/BApi";
import { DownloadTaskActionOnConflict } from "@/sdk/constants";

/** Switch modes and start under the server's task lock, retaining the task and its destination. */
export const downloadTaskWithBt = (
  id: number,
  actionOnConflict = DownloadTaskActionOnConflict.NotSet,
) => BApi.downloadTask.btDownloadTask(id, { actionOnConflict }, { showErrorToast: false });
