import BApi from "@/sdk/BApi";

export async function restoreCleanupReceipt(receiptId: string): Promise<void> {
  const response = await BApi.artifacts.restoreArtifactCleanup(encodeURIComponent(receiptId));
  if (response.code || response.data?.completed !== true)
    throw new Error(response.data?.message || response.message || "RESTORE_FAILED");
}
