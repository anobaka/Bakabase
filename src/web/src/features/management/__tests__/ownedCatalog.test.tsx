import { act, cleanup, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { ManagementSource } from "../api";
import {
  catalogItemKey,
  catalogOwnerSearch,
  canEditCatalogItem,
  useOwnedCatalog,
} from "../ownedCatalog";

const state = vi.hoisted(() => ({
  sources: [] as ManagementSource[],
  refresh: vi.fn(),
  read: vi.fn(),
  listeners: new Map<string, () => void>(),
}));
vi.mock("../DeviceDot", () => ({ default: () => null }));
vi.mock("../useManagementSources", () => ({
  useManagementSources: () => ({ sources: state.sources, refresh: state.refresh }),
}));
vi.mock("../resourceApi", () => ({
  createResourceApi: (source: ManagementSource) => ({ source }),
  subscribeResourceOwnerChanges: (source: ManagementSource, callback: () => void) => {
    state.listeners.set(source.serverId, callback);
    return () => state.listeners.delete(source.serverId);
  },
}));
const source = (serverId: string, offline = false): ManagementSource => ({
  serverId,
  libraryEpoch: `epoch-${serverId}`,
  name: serverId,
  available: !offline,
  isLocal: serverId === "local",
  contractVersion: 1,
  metadata: {
    identity: { serverId, libraryEpoch: `epoch-${serverId}` },
    name: serverId,
    denied: false,
    data: {
      activeGeneration: "complete",
      activeSequence: 2,
      resourceCount: 100000,
      stagedResourceCount: 0,
    },
    sync: { isOffline: offline, paused: false },
  },
});
const local = [{ id: 7, name: "Author", order: 4 }];
const read = (api: any) => state.read(api.source);
beforeEach(() => {
  state.sources = [source("local"), source("b"), source("c", true)];
  state.read.mockReset();
  state.listeners.clear();
});
afterEach(cleanup);

describe("catalog items remain ordinary owner-isolated data", () => {
  it("keeps same-ID same-name rows from every owner and lets the native page mix its normal sort", async () => {
    state.read.mockResolvedValue({ data: [{ id: 7, name: "Author", order: 1 }] });
    const { result } = renderHook(() => useOwnedCatalog(local, read));
    await act(async () => {});
    expect(result.current.items).toHaveLength(3);
    expect(new Set(result.current.items.map(catalogItemKey)).size).toBe(3);
    expect(
      [...result.current.items].sort((a, b) => a.order - b.order).map((r) => r.owner?.serverId),
    ).toEqual(["b", "c", "local"]);
    expect(state.read).toHaveBeenCalledTimes(2);
    expect(canEditCatalogItem(result.current.items.find((r) => r.owner?.serverId === "c")!)).toBe(
      false,
    );
    expect(catalogOwnerSearch(result.current.items[1].owner)).toEqual({
      owner: { serverId: "b", libraryEpoch: "epoch-b" },
      deviceIds: ["b"],
    });
  });
  it("removes revoked and changed-epoch data immediately even when an earlier read completes late", async () => {
    let complete!: (value: any) => void;
    state.read.mockImplementation((s) =>
      s.serverId === "b"
        ? new Promise((resolve) => {
            complete = resolve;
          })
        : Promise.resolve({ data: [{ id: 7, name: "c", order: 0 }] }),
    );
    const { result, rerender } = renderHook(() => useOwnedCatalog(local, read));
    await act(async () => {});
    state.sources = [
      source("local"),
      { ...source("b"), metadata: { ...source("b").metadata!, denied: true } },
    ];
    rerender();
    await act(async () => complete({ data: [{ id: 7, name: "stale", order: 0 }] }));
    expect(result.current.items.map((r) => r.name)).toEqual(["Author"]);
  });
  it("refreshes after an owner's successful edit and never lets a failed device erase local data", async () => {
    state.read.mockImplementation((s) =>
      s.serverId === "b"
        ? Promise.resolve({ data: [{ id: 8, name: "Before", order: 0 }] })
        : Promise.reject(new Error("unavailable")),
    );
    const { result } = renderHook(() => useOwnedCatalog(local, read));
    await act(async () => {});
    expect(result.current.errors.size).toBe(1);
    expect(result.current.items).toHaveLength(2);
    state.read.mockResolvedValue({ data: [{ id: 8, name: "After", order: 0 }] });
    await act(async () => state.listeners.get("b")!());
    expect(result.current.items[0].name).toBe("Author");
    expect(result.current.items.slice(1).map((r) => r.name)).toEqual(["After", "After"]);
  });
  it("does not refetch catalogs for progress-only polling but does refresh catalog-only changes", async () => {
    state.read.mockResolvedValue({ data: [] });
    const { rerender } = renderHook(() => useOwnedCatalog(local, read));
    await act(async () => {});
    state.sources = state.sources.map((s) => ({
      ...s,
      metadata: { ...s.metadata!, sync: { ...s.metadata!.sync, lastSyncedAtUtc: "changed" } },
    }));
    rerender();
    await act(async () => {});
    expect(state.read).toHaveBeenCalledTimes(2);
    state.sources = state.sources.map((s) => ({
      ...s,
      metadata: {
        ...s.metadata!,
        data: {
          ...s.metadata!.data,
          checkpoint: { generation: "complete", sequence: 2, complete: true, catalogRevision: 4 },
        },
      },
    }));
    rerender();
    await act(async () => {});
    expect(state.read).toHaveBeenCalledTimes(4);
  });
});
