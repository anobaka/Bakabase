import { act, cleanup, renderHook, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { useManagedResourceSearch } from "../useManagedResourceSearch";
import type { ManagementSource } from "../api";
import { InternalProperty, PropertyPool, SearchOperation } from "@/sdk/constants";
import type { Resource } from "@/core/models/Resource";

const mocks = vi.hoisted(() => ({
  sources: [] as ManagementSource[],
  owner: undefined as ManagementSource | undefined,
  search: vi.fn(),
  save: vi.fn(),
  get: vi.fn(),
}));
vi.mock("@/features/management/resourceSourcesContext", () => ({
  useResourceSources: () => ({ sources: mocks.sources }),
}));
vi.mock("@/features/management/resourceContext", () => ({ useResourceOwner: () => mocks.owner }));
vi.mock("@/sdk/BApi", () => ({
  default: { resource: { putSavedSearchCriteria: (...args: any[]) => mocks.save(...args) } },
}));
vi.mock("../resourceApi", () => ({
  createResourceApi: (source: ManagementSource) => ({
    resource: {
      searchResources: (...args: any[]) => mocks.search(source, ...args),
      getResourcesByKeys: (...args: any[]) => mocks.get(source, ...args),
    },
  }),
  subscribeResourceOwnerChanges: () => () => {},
}));
const source = (serverId: string, isLocal = false): ManagementSource => ({
  serverId,
  isLocal,
  libraryEpoch: `epoch-${serverId}`,
  name: serverId,
  available: true,
  contractVersion: 1,
});
beforeEach(() => {
  mocks.owner = undefined;
  mocks.sources = [source("local", true), source("remote")];
  mocks.search.mockReset().mockResolvedValue({ code: 0, data: [{ id: 1 }], totalCount: 1 });
  mocks.save.mockReset().mockResolvedValue({ code: 0 });
  mocks.get.mockReset().mockResolvedValue({ code: 0, data: [] });
});
afterEach(cleanup);

describe("unified search state", () => {
  it("retains displayed values while a saved remote edit is still waiting for replica refresh", async () => {
    mocks.owner = mocks.sources[1];
    const { result } = renderHook(useManagedResourceSearch);
    await act(() => result.current.search({ page: 1, pageSize: 50 }));
    const displayed = result.current.resources[0];
    mocks.get.mockRejectedValueOnce(new Error("MetadataRefreshPending"));
    await act(async () => {
      await expect(result.current.reloadResources([1])).rejects.toThrow("MetadataRefreshPending");
    });
    expect(result.current.resources).toEqual([displayed]);
    mocks.get.mockResolvedValueOnce({ code: 0, data: [{ id: 1, displayName: "saved value" }] });
    await act(() => result.current.reloadResources([1]));
    expect(result.current.resources[0].displayName).toBe("saved value");
  });
  it("keeps all/one/no-device results when default empty filters are restored or mounted again", async () => {
    mocks.search.mockImplementation(async (owner: ManagementSource) => {
      const count = owner.isLocal ? 2 : 4;
      return {
        code: 0,
        totalCount: count,
        data: Array.from({ length: count }, (_, index) => ({ id: count - index })),
      };
    });
    const { result } = renderHook(useManagedResourceSearch);
    const restored = {
      page: 1,
      pageSize: 50,
      group: {
        combinator: 1,
        disabled: false,
        filters: [
          {
            propertyPool: PropertyPool.Internal,
            propertyId: InternalProperty.Source,
            operation: SearchOperation.In,
            disabled: false,
          },
          {
            owner: mocks.sources[0],
            propertyPool: PropertyPool.Internal,
            propertyId: InternalProperty.MediaLibraryV2Multi,
            operation: SearchOperation.In,
            disabled: false,
          },
        ],
      },
    };
    await act(() => result.current.search(restored));
    expect(result.current.resources).toHaveLength(6);
    expect(result.current.response?.totalCount).toBe(6);
    expect(mocks.search.mock.calls.every(([, input]) => input.group === undefined)).toBe(true);
    await act(() => result.current.search(JSON.parse(JSON.stringify(restored))));
    expect(result.current.resources).toHaveLength(6);
    await act(() => result.current.search({ ...restored, deviceIds: ["remote"] }));
    expect(result.current.resources).toHaveLength(4);
    expect(
      result.current.resources.every((resource) => resource.owner?.serverId === "remote"),
    ).toBe(true);
    mocks.search.mockClear();
    await act(() => result.current.search({ ...restored, deviceIds: [] }));
    expect(result.current.resources).toEqual([]);
    expect(mocks.search).not.toHaveBeenCalled();
  });

  it("loads all devices when the restored API criteria serialize DeviceIds as null", async () => {
    const { result } = renderHook(useManagedResourceSearch);
    const form = JSON.parse(
      '{"page":1,"pageSize":50,"owner":null,"deviceIds":null,"group":null,"orders":null,"keyword":null,"tags":null}',
    );
    await act(() => result.current.search(form, { saveSearch: true, searchId: "saved" }));
    expect(mocks.search.mock.calls.map(([owner]) => owner.serverId)).toEqual(["local", "remote"]);
    expect(result.current.resources).toHaveLength(2);
    expect(mocks.save).toHaveBeenCalledWith(form, { id: "saved" });
    mocks.search.mockClear();
    await act(() => result.current.search({ ...form, deviceIds: [] }));
    expect(mocks.search).not.toHaveBeenCalled();
    expect(result.current.resources).toEqual([]);
  });

  it("reuses mixed-owner buffers on replace-page navigation but refreshes an explicitly repeated page", async () => {
    mocks.search.mockResolvedValue({
      code: 0,
      totalCount: 100,
      data: Array.from({ length: 100 }, (_, index) => ({ id: 100 - index })),
    });
    const { result } = renderHook(useManagedResourceSearch);
    await act(() => result.current.search({ page: 1, pageSize: 50 }));
    await act(() => result.current.search({ page: 2, pageSize: 50 }));
    expect(mocks.search).toHaveBeenCalledTimes(2);
    expect(result.current.resources[0].id).toBe(75);
    await act(() => result.current.search({ page: 1, pageSize: 50 }));
    expect(mocks.search).toHaveBeenCalledTimes(2);
    await act(() => result.current.search({ page: 1, pageSize: 50 }));
    expect(mocks.search).toHaveBeenCalledTimes(4);
  });

  it("inherits the scoped owner's identity for a legacy child-resource filter", async () => {
    mocks.owner = mocks.sources[1];
    const { result } = renderHook(useManagedResourceSearch);
    await act(() =>
      result.current.search({
        page: 1,
        pageSize: 50,
        group: {
          combinator: 1,
          disabled: false,
          filters: [
            {
              propertyPool: PropertyPool.Internal,
              propertyId: InternalProperty.ParentResource,
              operation: SearchOperation.Equals,
              dbValue: "7",
              disabled: false,
            },
          ],
        },
      }),
    );
    expect(mocks.search).toHaveBeenCalledTimes(1);
    expect(mocks.search.mock.calls[0][1].group.filters[0]).toMatchObject({
      propertyId: InternalProperty.ParentResource,
      dbValue: "7",
    });
  });

  it("does not let an earlier reload overwrite a later reload of the same owner/resource", async () => {
    mocks.owner = mocks.sources[1];
    const { result } = renderHook(useManagedResourceSearch);
    await act(() => result.current.search({ page: 1, pageSize: 50 }));
    let finishOld!: (value: any) => void;
    mocks.get
      .mockImplementationOnce(
        () =>
          new Promise((resolve) => {
            finishOld = resolve;
          }),
      )
      .mockResolvedValueOnce({ code: 0, data: [{ id: 1, displayName: "new" }] });
    let old!: Promise<void>;
    act(() => {
      old = result.current.reloadResources([1]);
    });
    await act(() => result.current.reloadResources([1]));
    await act(async () => {
      finishOld({ code: 0, data: [{ id: 1, displayName: "old" }] });
      await old;
    });
    expect(result.current.resources[0].displayName).toBe("new");
  });

  it("does not let a previous query's reload overwrite the same resource in a newer query", async () => {
    mocks.owner = mocks.sources[1];
    const { result } = renderHook(useManagedResourceSearch);
    await act(() => result.current.search({ page: 1, pageSize: 50, keyword: "old" }));
    let finishOld!: (value: any) => void;
    mocks.get.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          finishOld = resolve;
        }),
    );
    let old!: Promise<void>;
    act(() => {
      old = result.current.reloadResources([1]);
    });
    mocks.search.mockResolvedValue({
      code: 0,
      totalCount: 1,
      data: [{ id: 1, displayName: "new query" }],
    });
    await act(() => result.current.search({ page: 1, pageSize: 50, keyword: "new" }));
    await act(async () => {
      finishOld({ code: 0, data: [{ id: 1, displayName: "old query" }] });
      await old;
    });
    expect(result.current.resources[0].displayName).toBe("new query");
  });

  it("keeps a still-current resource refresh when another resource has a newer overlapping refresh", async () => {
    mocks.owner = mocks.sources[1];
    const { result } = renderHook(useManagedResourceSearch);
    await act(() =>
      result.current.setResources(
        [1, 2].map((id) => ({ id, owner: mocks.owner, displayName: "initial" }) as Resource),
      ),
    );
    let finishOld!: (value: any) => void;
    mocks.get
      .mockImplementationOnce(
        () =>
          new Promise((resolve) => {
            finishOld = resolve;
          }),
      )
      .mockResolvedValueOnce({ code: 0, data: [{ id: 1, displayName: "latest one" }] });
    let old!: Promise<void>;
    act(() => {
      old = result.current.reloadResources([1, 2]);
    });
    await act(() => result.current.reloadResources([1]));
    await act(async () => {
      finishOld({
        code: 0,
        data: [
          { id: 1, displayName: "stale one" },
          { id: 2, displayName: "latest two" },
        ],
      });
      await old;
    });
    expect(result.current.resources.map((resource) => resource.displayName)).toEqual([
      "latest one",
      "latest two",
    ]);
  });

  it("cancels an in-flight select-all scan when a new query starts", async () => {
    mocks.owner = mocks.sources[1];
    let finishSelection!: (value: any) => void;
    mocks.search
      .mockImplementationOnce(
        () =>
          new Promise((resolve) => {
            finishSelection = resolve;
          }),
      )
      .mockResolvedValue({ code: 0, totalCount: 1, data: [{ id: 2 }] });
    const { result } = renderHook(useManagedResourceSearch);
    let selection!: Promise<any>;
    act(() => {
      selection = result.current.searchAllResourceKeys({ page: 1, pageSize: 100, keyword: "old" });
    });
    await waitFor(() => expect(mocks.search).toHaveBeenCalledTimes(1));
    await act(() => result.current.search({ page: 1, pageSize: 50, keyword: "new" }));
    await act(async () => {
      finishSelection({ code: 0, totalCount: 100_000, data: [{ id: 1 }] });
      await expect(selection).rejects.toMatchObject({ name: "AbortError" });
    });
    expect(result.current.resources.map((resource) => resource.id)).toEqual([2]);
    expect(mocks.search).toHaveBeenCalledTimes(2);
  });

  it("publishes successful reload chunks even if a later chunk fails", async () => {
    mocks.owner = mocks.sources[1];
    const { result } = renderHook(useManagedResourceSearch);
    await act(() =>
      result.current.setResources(
        Array.from(
          { length: 125 },
          (_, index) => ({ id: index + 1, owner: mocks.owner, displayName: "old" }) as Resource,
        ),
      ),
    );
    mocks.get
      .mockImplementationOnce(async (_source, query) => ({
        code: 0,
        data: query.ids.map((id: number) => ({ id, displayName: "updated" })),
      }))
      .mockRejectedValueOnce(new Error("offline"));
    await act(async () => {
      await expect(
        result.current.reloadResources(Array.from({ length: 125 }, (_, index) => index + 1)),
      ).rejects.toThrow("offline");
    });
    expect(mocks.get.mock.calls.map(([, query]) => query.ids.length)).toEqual([100, 25]);
    expect(
      result.current.resources
        .slice(0, 100)
        .every((resource) => resource.displayName === "updated"),
    ).toBe(true);
    expect(
      result.current.resources.slice(100).every((resource) => resource.displayName === "old"),
    ).toBe(true);
  });

  it("searches each owner without saving there and saves the device filter on the viewer", async () => {
    const { result } = renderHook(useManagedResourceSearch);
    const form = { page: 1, pageSize: 50, deviceIds: ["remote"], keyword: "album" };
    await act(() => result.current.search(form, { saveSearch: true, searchId: "tab" }));
    expect(mocks.search).toHaveBeenCalledTimes(1);
    expect(mocks.search).toHaveBeenCalledWith(
      mocks.sources[1],
      expect.objectContaining({ pageSize: 100, keyword: "album" }),
      expect.objectContaining({ saveSearch: false }),
      expect.anything(),
    );
    expect(mocks.search.mock.calls[0][1]).not.toHaveProperty("deviceIds");
    expect(mocks.save).toHaveBeenCalledWith(form, { id: "tab" });
    expect(result.current.resources[0].owner?.serverId).toBe("remote");
  });
  it("scoped nested resource selection stays on its owner even inside the mixed page", async () => {
    mocks.owner = mocks.sources[1];
    const { result } = renderHook(useManagedResourceSearch);
    await act(() => result.current.search({ page: 1, pageSize: 20 }));
    expect(mocks.search).toHaveBeenCalledTimes(1);
    expect(mocks.search.mock.calls[0][0].serverId).toBe("remote");
    expect(mocks.save).not.toHaveBeenCalled();
  });
  it("prunes only the requested owner's deleted resource, not a local ID collision", async () => {
    const { result } = renderHook(useManagedResourceSearch);
    await act(() => result.current.search({ page: 1, pageSize: 20 }));
    expect(result.current.resources).toHaveLength(2);
    await act(() => result.current.reloadResources(['["remote","epoch-remote",1]']));
    expect(result.current.resources).toHaveLength(1);
    expect(result.current.resources[0].owner?.serverId).toBe("local");
  });
  it("ignores a late replaced search without clearing the current results or loading state", async () => {
    let finish: (value: any) => void = () => {};
    mocks.search.mockImplementation((_source, form) =>
      form.keyword === "old"
        ? new Promise((resolve) => {
            finish = resolve;
          })
        : Promise.resolve({ code: 0, data: [{ id: 2 }], totalCount: 1 }),
    );
    mocks.owner = mocks.sources[1];
    const { result } = renderHook(useManagedResourceSearch);
    let previous!: Promise<any>;
    act(() => {
      previous = result.current.search({ page: 1, pageSize: 20, keyword: "old" });
    });
    await waitFor(() => expect(mocks.search).toHaveBeenCalled());
    await act(() => result.current.search({ page: 1, pageSize: 20, keyword: "new" }));
    await act(async () => {
      finish({ code: 0, data: [{ id: 1 }], totalCount: 1 });
      await previous;
    });
    expect(result.current.resources.map((resource) => resource.id)).toEqual([2]);
    expect(result.current.loading).toBe(false);
    expect(result.current.sourceErrors).toEqual([]);
  });
});
