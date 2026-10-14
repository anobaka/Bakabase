import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { getManagementSources, ownedResourceKey, ownerClient, type ManagementSource } from "../api";
const { localSearch, localGet, localPut, localRequest } = vi.hoisted(() => ({
  localSearch: vi.fn(),
  localGet: vi.fn(),
  localPut: vi.fn(),
  localRequest: vi.fn(),
}));

vi.mock("@/sdk/BApi", () => ({
  default: {
    request: localRequest,
    resource: {
      searchResources: localSearch,
      getResourcesByKeys: localGet,
      putResourcePropertyValue: localPut,
    },
  },
}));
vi.mock("@/config/env", () => ({ default: { apiEndpoint: "http://manager" } }));
const source = (serverId: string, local = false): ManagementSource => ({
  serverId,
  name: serverId,
  libraryEpoch: `epoch-${serverId}`,
  isLocal: local,
  available: true,
  contractVersion: 1,
});

beforeEach(() => {
  vi.clearAllMocks();
});
afterEach(() => vi.unstubAllGlobals());

describe("source-bound management transport", () => {
  it("simultaneous owners with identical resource IDs never share a destination or identity", async () => {
    const fetcher = vi.fn().mockResolvedValue(new Response(JSON.stringify({ code: 0, data: [] })));

    // A distinct response is required for every JSON read.
    fetcher.mockImplementation(async () => new Response(JSON.stringify({ code: 0, data: [] })));
    vi.stubGlobal("fetch", fetcher);
    const a = source("a");
    const b = source("b");

    await Promise.all([
      ownerClient(a)("resourcePropertyPut", { id: 7, body: { value: "a" } }),
      ownerClient(b)("resourcePropertyPut", { id: 7, body: { value: "b" } }),
    ]);
    expect(fetcher.mock.calls.map(([url]) => url)).toEqual([
      "http://manager/federation/local/management/a/request",
      "http://manager/federation/local/management/b/request",
    ]);
    expect(fetcher.mock.calls.map(([, init]) => JSON.parse(init.body).libraryEpoch)).toEqual([
      "epoch-a",
      "epoch-b",
    ]);
    expect(ownedResourceKey(a, 7)).not.toBe(ownedResourceKey(b, 7));
  });
  it("captures the owner at creation instead of following later selection mutations", async () => {
    const fetcher = vi.fn(async (_url: string, _init: RequestInit) => new Response('{"code":0}'));

    vi.stubGlobal("fetch", fetcher);
    const target = source("b");
    const client = ownerClient(target);

    target.serverId = "a";
    target.libraryEpoch = "new";
    await client("resourceGet", { ids: [7] });
    expect(fetcher.mock.calls[0][0]).toContain("/b/request");
    expect(JSON.parse(String(fetcher.mock.calls[0][1].body)).libraryEpoch).toBe("epoch-b");
  });
  it("uses existing local SDK without changing its endpoint", async () => {
    const fetcher = vi.fn();

    vi.stubGlobal("fetch", fetcher);
    localSearch.mockResolvedValue({ code: 0, data: [] });
    await ownerClient(source("a", true))("resourceSearch", { body: { page: 1 } });
    expect(localSearch).toHaveBeenCalledWith(
      { page: 1 },
      { saveSearch: false, additionalItems: 2400 },
    );
    expect(fetcher).not.toHaveBeenCalled();
  });
  it("loads local editor metadata without expanding cover or playable file collections", async () => {
    localGet.mockResolvedValue({ code: 0, data: [] });
    await ownerClient(source("a", true))("resourceGet", { ids: [7] });
    expect(localGet).toHaveBeenCalledWith({ ids: [7], additionalItems: 2400 });
  });
  it("requests a bounded local browser-file page through the local SDK", async () => {
    localRequest.mockResolvedValue({ code: 0, data: { items: [], hasMore: false } });
    await ownerClient(source("a", true))("resourceBrowserFiles", {
      id: 7,
      query: { offset: ["100"], pageSize: ["100"] },
    });
    expect(localRequest).toHaveBeenCalledWith({
      path: "/management/library/resources/7/browser-files",
      method: "GET",
      format: "json",
      query: { offset: "100", pageSize: "100" },
    });
  });
  it("rejects unavailable sources without borrowing the local library", async () => {
    const fetcher = vi.fn();

    vi.stubGlobal("fetch", fetcher);
    await expect(
      ownerClient({ ...source("b"), available: false })("resourcePropertyPut", { id: 7 }),
    ).rejects.toThrow("unavailable");
    expect(localPut).not.toHaveBeenCalled();
    expect(fetcher).not.toHaveBeenCalled();
  });
  it("surfaces source-side HTTP and application refusals as failures", async () => {
    const fetcher = vi
      .fn()
      .mockResolvedValueOnce(new Response('{"message":"Identity changed"}', { status: 409 }))
      .mockResolvedValueOnce(new Response('{"code":403,"message":"Revoked"}'));

    vi.stubGlobal("fetch", fetcher);
    const client = ownerClient(source("b"));

    await expect(client("resourceGet", { ids: [7] })).rejects.toThrow("Identity changed");
    await expect(client("resourcePropertyPut", { id: 7 })).rejects.toThrow("Revoked");
    expect(localPut).not.toHaveBeenCalled();
  });
  it("rejects malformed source listings rather than implying full coverage", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async (_url: string, _init: RequestInit) => new Response('{"code":0}')),
    );
    await expect(getManagementSources()).rejects.toThrow("Invalid management sources");
  });
});
