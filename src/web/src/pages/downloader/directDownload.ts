import type { BootstrapModelsResponseModelsBaseResponse } from "@/sdk/Api";

import BApi from "@/sdk/BApi";
import { ContentType } from "@/sdk/Api";
import { DownloadTaskActionOnConflict } from "@/sdk/constants";

export const downloadTaskDirectly = (
  id: number,
  actionOnConflict = DownloadTaskActionOnConflict.NotSet,
) =>
  BApi.request<BootstrapModelsResponseModelsBaseResponse, unknown>({
    path: `/download-task/${id}/direct-download`,
    method: "POST",
    body: { actionOnConflict },
    type: ContentType.Json,
    format: "json",
    // The row keeps its pending state and surfaces failures, including transport errors.
    showErrorToast: false,
  });
