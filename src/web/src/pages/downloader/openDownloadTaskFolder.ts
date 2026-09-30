import type { DownloadTask } from "@/core/models/DownloadTask";
import type { BootstrapModelsResponseModelsBaseResponse } from "@/sdk/Api";

import BApi from "@/sdk/BApi";
import { ResponseCode, ThirdPartyId } from "@/sdk/constants";

type OpenTargetResponse = BootstrapModelsResponseModelsBaseResponse & {
  data?: { path: string; openInDirectory: boolean };
};

export const openDownloadTaskFolder = async (
  task: Pick<DownloadTask, "id" | "thirdPartyId" | "downloadPath">,
) => {
  if (task.thirdPartyId !== ThirdPartyId.ExHentai) {
    if (task.downloadPath) {
      await BApi.tool.openFileOrDirectory({ path: task.downloadPath });
    }

    return;
  }

  const response = await BApi.request<OpenTargetResponse, unknown>({
    path: `/download-task/${task.id}/open-target`,
    method: "GET",
    format: "json",
  });

  if (response.code === ResponseCode.Success && response.data?.path) {
    // Keep native folder opening on the user's machine through the existing tool API.
    await BApi.tool.openFileOrDirectory(response.data);
  }
};
