import { afterEach, describe, expect, it, vi } from "vitest";

import { federationRequest } from "../transport";
import { federationPeerApi } from "../peerApi";
import { federationQueryApi } from "../queryApi";
import { federationResourceApi, localMediaUrl } from "../resourceApi";
import { readResourceRef, withResourceRef } from "../navigation";
import { resourceKey, sameResource } from "../types";

vi.mock("@/config/env", () => ({ default: { apiEndpoint: "http://localhost:5555" } }));
afterEach(() => vi.unstubAllGlobals());

describe("local federation transport", () => {
  it("reads raw DTOs and preserves per-node failures rather than claiming an empty result", async () => {
    const omittedNodes = [{ nodeId: "offline", code: "Offline", retryable: true }];
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({ identity: { nodeId: "local" } })))
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({ code: "NoParticipants", message: "No source completed", omittedNodes }),
          { status: 503 },
        ),
      );

    vi.stubGlobal("fetch", fetch);
    expect(await federationRequest("/peers")).toEqual({ identity: { nodeId: "local" } });
    await expect(federationRequest("/queries")).rejects.toMatchObject({
      code: "NoParticipants",
      omittedNodes,
    });
    expect(fetch.mock.calls[0][0]).toBe("http://localhost:5555/federation/local/peers");
  });
  it("releases sessions through a 204 response and carries complete refs when resolving or playing", async () => {
    const fetch = vi
      .fn()
      .mockImplementation(() => Promise.resolve(new Response(null, { status: 204 })));

    vi.stubGlobal("fetch", fetch);
    await expect(federationQueryApi.release("a/b")).resolves.toBeUndefined();
    const ref = { nodeId: "remote", libraryEpoch: "old", resourceId: 1 };

    await federationResourceApi.detail(ref);
    await federationResourceApi.playback(ref, "opaque", "player");
    expect(fetch.mock.calls[0]).toEqual([
      "http://localhost:5555/federation/local/queries/a%2Fb",
      expect.objectContaining({ method: "DELETE", keepalive: true }),
    ]);
    expect(JSON.parse(fetch.mock.calls[1][1].body)).toEqual({ refs: [ref] });
    await federationResourceApi.openDirectory(ref);
    expect(fetch.mock.calls[3][0]).toBe(
      "http://localhost:5555/federation/local/resources/open-directory",
    );
    expect(fetch.mock.calls[3][1].method).toBe("POST");
    expect(JSON.parse(fetch.mock.calls[3][1].body)).toEqual({ resourceRef: ref });
    expect(JSON.parse(fetch.mock.calls[2][1].body)).toEqual({
      assetRef: { resourceRef: ref, assetId: "opaque" },
      mode: "player",
    });
  });
  it("sends expected mappings so a concurrent edit cannot silently overwrite saved paths", async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));

    vi.stubGlobal("fetch", fetch);
    const existing = [{ sourceRootId: "root", localPath: "/Old" }];
    const next = [{ sourceRootId: "root", localPath: "/New" }];

    await federationPeerApi.mappings("peer", next, existing);
    expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({
      mappings: next,
      expectedMappings: existing,
    });
    expect(fetch.mock.calls[0][1].method).toBe("PUT");
  });
  it("only renders media capabilities issued on this coordinator", () => {
    expect(localMediaUrl("/federation/local/media/opaque")).toBe(
      "http://localhost:5555/federation/local/media/opaque",
    );
    for (const path of [
      "https://remote.example/federation/local/media/key",
      "//evil.example/federation/local/file",
      "/resource/1/file",
      "javascript:alert(1)",
    ])
      expect(() => localMediaUrl(path)).toThrow();
  });
});

describe("resource navigation identity", () => {
  it("keeps owner and epoch alongside colliding numeric IDs in keys and deep links", () => {
    const ref = { nodeId: "node:with/slash", libraryEpoch: "epoch:new", resourceId: 1 };
    const params = withResourceRef(new URLSearchParams("scope=all"), ref);

    expect(params.get("scope")).toBe("all");
    expect(readResourceRef(params)).toEqual(ref);
    expect(sameResource(ref, { ...ref, nodeId: "local" })).toBe(false);
    expect(resourceKey(ref)).not.toBe(resourceKey({ ...ref, libraryEpoch: "epoch:old" }));
    expect(readResourceRef(withResourceRef(params))).toBeUndefined();
    expect(
      readResourceRef(new URLSearchParams("node=x&epoch=y&resource=9007199254740993")),
    ).toBeUndefined();
  });
});
