import { afterEach, describe, expect, it, vi } from "vitest";

import { clientApi, ClientApiError } from "../clientApi";

afterEach(() => vi.unstubAllGlobals());

describe("the active relay's path mapping editor contract", () => {
  it("sends the captured baseline when replacing mappings", async () => {
    const fetcher = vi.fn().mockResolvedValue(Response.json({ code: 0, data: { changed: true } }));

    vi.stubGlobal("fetch", fetcher);
    const mappings = [{ serverPath: "/owner", localPath: "Z:\\media" }];

    await clientApi.setPathMappings("source-device", mappings, []);
    expect(fetcher).toHaveBeenCalledWith("/client/servers/source-device/path-mappings", {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ mappings, expectedMappings: [] }),
    });
  });

  it("keeps the conflict reason for the shared editor instead of reporting a successful save", async () => {
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValue(
          Response.json({ code: 409, message: "PathMappingsChanged" }, { status: 409 }),
        ),
    );
    const failure = await clientApi.setPathMappings("source", [], []).catch((error) => error);

    expect(failure).toBeInstanceOf(ClientApiError);
    expect(failure).toMatchObject({ status: 409, reason: "PathMappingsChanged" });
  });

  it("bounds directory metadata reads, encodes branches and keeps cancellation", async () => {
    const page = { libraryEpoch: "epoch", nodes: [], nextCursor: null };
    const fetcher = vi.fn().mockResolvedValue(Response.json({ code: 0, data: page }));
    const abort = new AbortController();

    vi.stubGlobal("fetch", fetcher);
    expect(
      await clientApi.mappingTree(
        "source/device",
        { parent: "Z:\\media & video", cursor: "cursor_1", libraryEpoch: "epoch" },
        abort.signal,
      ),
    ).toEqual(page);
    const [url, options] = fetcher.mock.calls[0];
    const query = new URL(url, "http://localhost");

    expect(query.pathname).toBe("/client/servers/source%2Fdevice/path-mapping-tree");
    expect(Object.fromEntries(query.searchParams)).toEqual({
      limit: "200",
      parent: "Z:\\media & video",
      cursor: "cursor_1",
      libraryEpoch: "epoch",
    });
    expect(options.signal).toBe(abort.signal);
  });
});
