import { afterEach, describe, expect, it, vi } from "vitest";

import { managedServerApi } from "../serverApi";
import { federationPeerApi } from "../peerApi";

vi.mock("@/config/env", () => ({ default: { apiEndpoint: "http://localhost:5555" } }));
afterEach(() => vi.unstubAllGlobals());
const response = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status });

describe("managed device name transport", () => {
  it("binds the selected server and current epoch, never patches a local alias or identity", async () => {
    const snapshot = { name: "NAS", libraryEpoch: "epoch-one", environmentControlled: false };
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(
        response({
          sources: [
            { serverId: "local", libraryEpoch: "local-epoch", available: true },
            { serverId: "owner", libraryEpoch: "epoch-one", available: true },
          ],
        }),
      )
      .mockResolvedValueOnce(response(snapshot))
      .mockResolvedValueOnce(response({ ...snapshot, name: "New NAS" }));

    vi.stubGlobal("fetch", fetch);
    expect(await managedServerApi.getDeviceName("owner")).toEqual(snapshot);
    await managedServerApi.setDeviceName("owner", snapshot, "New NAS");
    expect(fetch.mock.calls[1][0]).toBe(
      "http://localhost:5555/federation/local/management/owner/request",
    );
    expect(JSON.parse(fetch.mock.calls[1][1].body)).toEqual({
      libraryEpoch: "epoch-one",
      operation: "deviceNameGet",
    });
    expect(fetch.mock.calls[2][0]).toBe(fetch.mock.calls[1][0]);
    expect(JSON.parse(fetch.mock.calls[2][1].body)).toEqual({
      libraryEpoch: "epoch-one",
      operation: "deviceNameSet",
      body: { name: "New NAS", expectedName: "NAS" },
    });
  });
  it("does not substitute a local device when the requested owner is unavailable", async () => {
    const fetch = vi.fn().mockResolvedValueOnce(
      response({
        sources: [{ serverId: "owner", available: false, errorCode: "ManagementUpdateRequired" }],
      }),
    );

    vi.stubGlobal("fetch", fetch);
    await expect(managedServerApi.getDeviceName("owner")).rejects.toMatchObject({
      code: "ManagementUpdateRequired",
    });
    expect(fetch).toHaveBeenCalledTimes(1);
  });
  it("preserves the existing local endpoint and sends an optional observed name", async () => {
    const fetch = vi.fn().mockResolvedValue(response({}));

    vi.stubGlobal("fetch", fetch);
    await federationPeerApi.setName("New", "Old");
    expect(fetch.mock.calls[0][0]).toBe("http://localhost:5555/federation/local/peers/name");
    expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({ name: "New", expectedName: "Old" });
  });
});
