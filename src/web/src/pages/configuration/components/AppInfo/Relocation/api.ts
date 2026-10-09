import type { BootstrapModelsResponseModelsBaseResponse } from "@/sdk/Api";
import type { AppDataImportStatus } from "../Import/api";

import BApi from "@/sdk/BApi";

interface SetupResponse extends BootstrapModelsResponseModelsBaseResponse {
  data?: { setupToken: string; setupUrl?: string };
}

interface StatusResponse extends BootstrapModelsResponseModelsBaseResponse {
  data?: AppDataImportStatus;
}

const path = "/app/data-path/relocation";

export const getAppDataRelocationStatus = (signal?: AbortSignal) =>
  BApi.request<StatusResponse, unknown>({
    path,
    method: "GET",
    format: "json",
    showErrorToast: false,
    signal,
  });

export const createAppDataRelocationSetupSession = () =>
  BApi.request<SetupResponse, unknown>({
    path: `${path}/setup-session`,
    method: "POST",
    format: "json",
    showErrorToast: false,
  });

export const cancelAppDataRelocation = () =>
  BApi.request<BootstrapModelsResponseModelsBaseResponse, unknown>({
    path,
    method: "DELETE",
    format: "json",
    showErrorToast: false,
  });
