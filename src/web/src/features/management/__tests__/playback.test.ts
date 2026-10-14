import type { ManagementSource } from "../api";

import { afterEach, describe, expect, it, vi } from "vitest";

import { managedMediaUrl, playManagedResource } from "../playback";

import { DataOrigin } from "@/sdk/constants";

const source: ManagementSource = {
  serverId: "owner",
  name: "Owner",
  libraryEpoch: "epoch",
  isLocal: false,
  available: true,
  contractVersion: 1,
};
const item = { origin: DataOrigin.FileSystem, key: "/library/a & b/映像.mkv" };

afterEach(() => vi.unstubAllGlobals());

describe("owner-bound playback transport", () => {
  it("encodes the owner, generation and exact candidate without interpreting its filename as a URL", () => {
    const url = new URL(managedMediaUrl(source, 42, item), window.location.origin);

    expect(url.pathname).toBe("/federation/local/management/owner/media");
    expect(Object.fromEntries(url.searchParams)).toEqual({
      libraryEpoch: "epoch",
      resourceId: "42",
      origin: String(DataOrigin.FileSystem),
      key: item.key,
    });
  });
  it("requests local manager execution with owner identity, never the remote resource's execution API", async () => {
    const fetch = vi
      .fn()
      .mockResolvedValue(new Response(JSON.stringify({ code: 0 }), { status: 200 }));

    vi.stubGlobal("fetch", fetch);
    await playManagedResource(source, 42, item, true);
    expect(fetch.mock.calls[0][0]).toBe("/federation/local/management/owner/play");
    const options = fetch.mock.calls[0][1];

    expect(options.method).toBe("POST");
    expect(JSON.parse(options.body)).toEqual({
      libraryEpoch: "epoch",
      resourceId: 42,
      origin: item.origin,
      key: item.key,
      explicitChoice: true,
    });
  });
  it.each([200, 409])(
    "surfaces native launch failures even when the HTTP status is %s",
    async (status) => {
      vi.stubGlobal(
        "fetch",
        vi.fn().mockResolvedValue(
          new Response(JSON.stringify({ code: 409, message: "Map this library first" }), {
            status,
          }),
        ),
      );
      await expect(playManagedResource(source, 42, item)).rejects.toThrow("Map this library first");
    },
  );
});
