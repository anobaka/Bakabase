import type { DownloadTask } from "@/core/models/DownloadTask";
import type { BootstrapModelsResponseModelsBaseResponse } from "@/sdk/Api";

import BApi from "@/sdk/BApi";
import { ResponseCode, ThirdPartyId } from "@/sdk/constants";
import { resourceFolderPath } from "@/components/Resource/resourceFolderPath";

type OpenTargetResponse = BootstrapModelsResponseModelsBaseResponse & {
  data?: { path: string; openInDirectory: boolean };
};

export class DownloadFolderUnavailableError extends Error {}

type FolderActions = {
  userSideActionsRunHere: boolean;
  showLocation: (path: string) => void;
};

export const openDownloadTaskFolder = async (
  task: Pick<DownloadTask, "id" | "thirdPartyId" | "downloadPath">,
  actions: FolderActions,
) => {
  let target: { path: string; openInDirectory?: boolean };

  if (task.thirdPartyId !== ThirdPartyId.ExHentai) {
    if (!task.downloadPath) throw new DownloadFolderUnavailableError();
    target = { path: task.downloadPath };
  } else {
    const response = await BApi.request<OpenTargetResponse, unknown>({
      path: `/download-task/${task.id}/open-target`,
      method: "GET",
      format: "json",
      showErrorToast: false,
    });

    if (response.code === ResponseCode.NotFound || !response.data?.path) {
      if (response.code && response.code !== ResponseCode.NotFound && response.message)
        throw new Error(response.message);
      throw new DownloadFolderUnavailableError();
    }
    if (response.code !== ResponseCode.Success)
      throw new Error(response.message ?? `API ${response.code}`);
    target = response.data;
  }

  if (actions.userSideActionsRunHere) {
    // Keep native folder opening on the user's machine through the existing tool API.
    const response = await BApi.tool.openFileOrDirectory(target, { showErrorToast: false });

    if (response.code) throw new Error(response.message ?? `API ${response.code}`);
  } else {
    actions.showLocation(resourceFolderPath(target.path, target.openInDirectory));
  }
};
