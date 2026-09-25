import { beforeEach, describe, expect, it, vi } from "vitest";

import { dataSyncApi, DataSyncProblemError, DataSyncRequestError, isRefusedHere } from "../api";

import BApi from "@/sdk/BApi";
import { DataSyncProblemCode } from "@/sdk/constants";

vi.mock("@/sdk/BApi", () => ({
  default: {
    dataSync: {
      getDataSyncOverview: vi.fn(),
      getDataSyncLinks: vi.fn(),
      setDataSyncSharing: vi.fn(),
      createDataSyncLink: vi.fn(),
      syncDataSyncNow: vi.fn(),
      createDataSyncInvitation: vi.fn(),
    },
  },
}));

const sdk = BApi.dataSync as unknown as Record<string, ReturnType<typeof vi.fn>>;

/** What the SDK throws for an HTTP failure: the response, with the body it read. */
const refused = (status: number, reason?: string) => {
  const response = new Response(null, {
    status,
    headers: reason ? { "X-Bakabase-Remote-Access": reason } : {},
  }) as Response & { error?: unknown };

  response.error = { code: 401, message: "This action runs on the machine hosting Bakabase." };

  return response;
};

beforeEach(() => vi.clearAllMocks());

describe("data sync's calls", () => {
  it("answer the record, and show nothing by themselves", async () => {
    sdk.getDataSyncOverview.mockResolvedValue({ code: 0, data: { deviceName: "NAS" } });

    await expect(dataSyncApi.overview()).resolves.toEqual({ deviceName: "NAS" });
    expect(sdk.getDataSyncOverview).toHaveBeenCalledWith({ showErrorToast: false });
  });

  it("answer an empty list for a list that came back empty", async () => {
    sdk.getDataSyncLinks.mockResolvedValue({ code: 0 });

    await expect(dataSyncApi.links()).resolves.toEqual([]);
  });

  it("say the gate refused this window", async () => {
    sdk.getDataSyncOverview.mockRejectedValue(refused(403, "HostOnly"));
    const error = await dataSyncApi.overview().catch((cause) => cause);

    expect(error).toBeInstanceOf(DataSyncRequestError);
    expect(error).toMatchObject({ code: "HostOnly", status: 403 });
    expect(isRefusedHere(error)).toBe(true);
  });

  it("tell other failures apart from the gate's", async () => {
    sdk.getDataSyncOverview.mockRejectedValue(refused(500));
    const failed = await dataSyncApi.overview().catch((cause) => cause);

    expect(failed).toMatchObject({ code: "Http500", status: 500 });
    expect(isRefusedHere(failed)).toBe(false);

    sdk.getDataSyncOverview.mockRejectedValue(new TypeError("Failed to fetch"));
    await expect(dataSyncApi.overview()).rejects.toMatchObject({ code: "Network" });

    sdk.getDataSyncOverview.mockResolvedValue({ code: 500, message: "boom" });
    await expect(dataSyncApi.overview()).rejects.toMatchObject({ code: "Server", message: "boom" });
  });

  it("let a cancelled request stay cancelled", async () => {
    const aborted = new DOMException("Aborted", "AbortError");

    sdk.getDataSyncOverview.mockRejectedValue(aborted);
    await expect(dataSyncApi.overview()).rejects.toBe(aborted);
  });

  it("throw an action's expected failure as a problem", async () => {
    sdk.setDataSyncSharing.mockResolvedValue({
      code: 0,
      data: { code: DataSyncProblemCode.NotAllowedOnThisDevice },
    });
    const error = await dataSyncApi
      .setSharing({ enabled: true, enablePairedRemoteAccess: false })
      .catch((cause) => cause);

    expect(error).toBeInstanceOf(DataSyncProblemError);
    expect(error.label).toBe("NotAllowedOnThisDevice");

    sdk.createDataSyncLink.mockResolvedValue({
      code: 0,
      data: { problem: { code: DataSyncProblemCode.LinkExists } },
    });
    await expect(
      dataSyncApi.createLink({ peerNodeId: "n", mode: 1, kinds: ["customProperty"] }),
    ).rejects.toBeInstanceOf(DataSyncProblemError);
  });

  it("answer nothing for an action that worked", async () => {
    sdk.setDataSyncSharing.mockResolvedValue({ code: 0 });

    await expect(
      dataSyncApi.setSharing({ enabled: false, enablePairedRemoteAccess: false }),
    ).resolves.toBeUndefined();
  });

  it("always send a body to sync now", async () => {
    sdk.syncDataSyncNow.mockResolvedValue({ code: 0, data: { taskId: "DataSync" } });

    await dataSyncApi.syncNow();
    expect(sdk.syncDataSyncNow).toHaveBeenCalledWith({}, { showErrorToast: false });
    await dataSyncApi.syncNow(3);
    expect(sdk.syncDataSyncNow).toHaveBeenLastCalledWith({ linkId: 3 }, { showErrorToast: false });
  });

  it("answer the code itself for a new invitation", async () => {
    sdk.createDataSyncInvitation.mockResolvedValue({
      code: 0,
      data: { invitation: { code: "12345678", expiresAt: "", addresses: [], allowTwoWay: false } },
    });

    await expect(dataSyncApi.createInvitation(false)).resolves.toMatchObject({ code: "12345678" });
  });
});
