import BApi from "@/sdk/BApi";

export type CleanupRestoreStatus = "Restored" | "RecoveryLocationUnavailable";

export async function restoreCleanupReceipt(receiptId: string): Promise<CleanupRestoreStatus> {
  const response = await BApi.artifacts.restoreArtifactCleanup(encodeURIComponent(receiptId));
  if (!response.code && response.data?.completed === false && response.data.status === "RecoveryLocationUnavailable")
    return response.data.status;
  if (response.code || response.data?.completed !== true)
    throw new Error(response.data?.message || response.message || "RESTORE_FAILED");
  return "Restored";
}
