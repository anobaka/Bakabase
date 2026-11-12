import { beforeEach, describe, expect, it, vi } from "vitest";
import { restoreCleanupReceipt } from "../artifactCleanupApi";
import BApi from "@/sdk/BApi";

vi.mock("@/sdk/BApi", () => ({ default: { artifacts: { restoreArtifactCleanup: vi.fn() } } }));

const restore = vi.mocked(BApi.artifacts.restoreArtifactCleanup);

describe("artifact cleanup recovery", () => {
  beforeEach(() => restore.mockReset());

  it("uses the authenticated API transport and a receipt ID, without accepting file paths", async () => {
    restore.mockResolvedValue({ code: 0, data: { completed: true, status: "Restored" } });
    await restoreCleanupReceipt("receipt/one");
    expect(restore).toHaveBeenCalledWith("receipt%2Fone");
  });

  it("preserves a server restore failure for a later retry", async () => {
    restore.mockResolvedValue({ code: 1, message: "Existing file differs" });
    await expect(restoreCleanupReceipt("receipt-one")).rejects.toThrow("Existing file differs");
  });

  it.each(["Blocked", "Pending"])(
    "treats code-zero %s as incomplete and permits a later successful retry",
    async (status) => {
      restore
        .mockResolvedValueOnce({
          code: 0,
          data: { completed: false, status, message: "Archive is still in use" },
        })
        .mockResolvedValueOnce({ code: 0, data: { completed: true, status: "Restored" } });
      await expect(restoreCleanupReceipt("receipt-one")).rejects.toThrow("Archive is still in use");
      await expect(restoreCleanupReceipt("receipt-one")).resolves.toBeUndefined();
      expect(restore.mock.calls).toEqual([["receipt-one"], ["receipt-one"]]);
    },
  );

  it("requires an explicit completed result even when the response code is zero", async () => {
    restore.mockResolvedValue({ code: 0 });
    await expect(restoreCleanupReceipt("receipt-one")).rejects.toThrow("RESTORE_FAILED");
  });
});
