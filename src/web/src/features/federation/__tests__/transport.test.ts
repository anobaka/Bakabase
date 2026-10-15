import { afterEach, describe, expect, it, vi } from "vitest";

import { federationPeerApi } from "../peerApi";
import { readResourceRef, withResourceRef } from "../navigation";

vi.mock("@/config/env", () => ({ default: { apiEndpoint: "http://localhost:5555" } }));
afterEach(() => vi.unstubAllGlobals());

describe("local federation transport", () => {
  it.each([
    [false, false],
    [true, false],
    [true, true],
  ])(
    "keeps the restore/clone choice explicit on the wire (new node: %s, new install identity: %s)",
    async (asNewNode, replaceInstallIdentity) => {
      const fetch = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));

      vi.stubGlobal("fetch", fetch);
      await federationPeerApi.resetIdentity(asNewNode, replaceInstallIdentity);
      expect(fetch.mock.calls[0][0]).toBe(
        "http://localhost:5555/federation/local/peers/identity/reset",
      );
      expect(fetch.mock.calls[0][1].method).toBe("POST");
      expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({
        asNewNode,
        replaceInstallIdentity,
      });
    },
  );
});

describe("resource navigation identity", () => {
  it("keeps owner and epoch alongside colliding numeric IDs in keys and deep links", () => {
    const ref = { nodeId: "node:with/slash", libraryEpoch: "epoch:new", resourceId: 1 };
    const params = withResourceRef(new URLSearchParams("scope=all"), ref);

    expect(params.get("scope")).toBe("all");
    expect(readResourceRef(params)).toEqual(ref);
    expect(readResourceRef(withResourceRef(params))).toBeUndefined();
    expect(
      readResourceRef(new URLSearchParams("node=x&epoch=y&resource=9007199254740993")),
    ).toBeUndefined();
  });
});
