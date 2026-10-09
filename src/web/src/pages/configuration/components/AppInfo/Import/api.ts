import type { BootstrapModelsResponseModelsBaseResponse } from "@/sdk/Api";

import BApi from "@/sdk/BApi";

export type AppDataImportPhase =
  | "queued"
  | "stopping"
  | "scanning"
  | "copying"
  | "verifying"
  | "backing-up"
  | "installing"
  | "starting"
  | "completed"
  | "failed"
  | "cancelled";

export interface AppDataImportProgress {
  id: string;
  phase: AppDataImportPhase;
  completedFiles: number;
  totalFiles: number;
  completedBytes: number;
  totalBytes: number;
  currentFile?: string;
  completedEntries: number;
  totalEntries: number;
  elapsedSeconds: number;
  bytesPerSecond: number;
  remainingSeconds?: number;
  updatedAtUtc: string;
  startedAtUtc?: string;
  backupPath?: string;
  sourcePath?: string;
  targetPath?: string;
  error?: string;
}

export interface AppDataImportStatus {
  supported: boolean;
  sourcePath?: string;
  targetPath?: string;
  originalDataPath?: string;
  currentPath: string;
  progress?: AppDataImportProgress;
  monitorToken?: string;
  monitorUrl?: string;
  automaticMaintenance?: boolean;
}

interface ImportResponse<T> extends BootstrapModelsResponseModelsBaseResponse {
  data?: T;
}

const path = "/app/data-path/import";

export const getAppDataImportStatus = (signal?: AbortSignal) =>
  BApi.request<ImportResponse<AppDataImportStatus>, unknown>({
    path,
    method: "GET",
    format: "json",
    showErrorToast: false,
    signal,
  });

const backendPageUrl = (pagePath: string) => {
  const backend = new URL(BApi.baseUrl || window.location.origin, window.location.origin);

  return new URL(`${backend.pathname.replace(/\/$/, "")}${pagePath}`, backend.origin);
};
const setupLanguage = (language: string) => (/^(cn|zh)/i.test(language) ? "cn" : "en");
const backendProvidedPageUrl = (page: string, fallback: string) =>
  page.startsWith("/") && !page.startsWith("//")
    ? backendPageUrl(page)
    : new URL(page, backendPageUrl(fallback));

/** The read-only monitor capability stays in the fragment, never in the HTTP URL. */
export const getAppDataImportMonitorUrl = (
  token: string,
  language: string,
  monitorUrl?: string,
) => {
  const url = monitorUrl
    ? backendProvidedPageUrl(monitorUrl, `${path}/progress`)
    : backendPageUrl(`${path}/progress`);

  if (url.protocol !== "http:" && url.protocol !== "https:") {
    throw new Error("The setup service returned an invalid progress address.");
  }
  url.username = "";
  url.password = "";
  url.search = "";

  url.hash = `token=${encodeURIComponent(token)}&lang=${setupLanguage(language)}`;

  return url.href;
};

export const createAppDataImportSetupSession = () =>
  BApi.request<ImportResponse<{ setupToken: string; setupUrl?: string }>, unknown>({
    path: `${path}/setup-session`,
    method: "POST",
    format: "json",
    showErrorToast: false,
  });

export const getAppDataSetupUrl = (setupToken: string, language: string, setupUrl?: string) => {
  const url = setupUrl ? backendProvidedPageUrl(setupUrl, "/setup") : backendPageUrl("/setup");

  if (url.protocol !== "http:" && url.protocol !== "https:") {
    throw new Error("The setup service returned an invalid address.");
  }
  url.username = "";
  url.password = "";
  url.search = "";

  url.hash = `setupToken=${encodeURIComponent(setupToken)}&lang=${setupLanguage(language)}`;

  return url.href;
};

export const getAppDataImportSetupUrl = getAppDataSetupUrl;

export const navigateToAppDataSetup = (url: string) => window.location.assign(url);

export const cancelAppDataImport = () =>
  BApi.request<BootstrapModelsResponseModelsBaseResponse, unknown>({
    path,
    method: "DELETE",
    format: "json",
    showErrorToast: false,
  });
