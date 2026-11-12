import BApi from "@/sdk/BApi";
import type { AcquireAdapter } from "./models";

export async function requireIntentResult<T>(
  request: Promise<{ code?: number; message?: string | null; data?: T }>,
): Promise<T> {
  const response = await request;
  if (response.code || !response.data)
    throw new Error(response.message || "DOWNLOAD_INTENT_FAILED");
  return response.data;
}
export const defaultAcquireAdapter: AcquireAdapter = {
  preflight: (request, directory) =>
    requireIntentResult(
      BApi.downloadIntents.preflightDownloadIntent(request, {
        outputDirectory: directory || undefined,
      }),
    ),
  submit: (request, directory) =>
    requireIntentResult(
      BApi.downloadIntents.submitDownloadIntent(request, {
        outputDirectory: directory || undefined,
      }),
    ),
};
