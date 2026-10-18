import type { PropsWithChildren } from "react";
import type { ReferenceProperty } from "@/components/Property/referenceValues";
import type { ManagementSource } from "@/features/management/api";
import type { SearchForm } from "@/pages/resource/models";

import { act, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { useReferenceValueResourceCounts } from "../useReferenceValueResourceCounts";

import { GroupCombinator } from "@/components/ResourceFilter/models";
import { serializeStandardValue } from "@/components/StandardValue/helpers";
import { mediaLibraryChoiceValue } from "@/features/management/mediaLibraryFilter";
import { ResourceOwnerProvider } from "@/features/management/resourceContext";
import { ResourceSourcesProvider } from "@/features/management/resourceSourcesContext";
import { normalizeQueryOwner } from "@/features/management/resourceQuery";
import {
  InternalProperty,
  PropertyPool,
  PropertyType,
  SearchOperation,
  StandardValueType,
} from "@/sdk/constants";

const { request } = vi.hoisted(() => ({ request: vi.fn() }));

vi.mock("@/features/management/resourceApi", () => ({
  createResourceApi: (source?: ManagementSource) => ({
    request: (input: unknown) => request(source, input),
  }),
  subscribeResourceOwnerChanges: () => () => {},
}));

const local: ManagementSource = {
  serverId: "local",
  libraryEpoch: "local-epoch",
  name: "This device",
  isLocal: true,
  available: true,
  contractVersion: 1,
};
const peer: ManagementSource = {
  ...local,
  serverId: "peer",
  libraryEpoch: "peer-epoch",
  name: "Another device",
  isLocal: false,
};
const property: ReferenceProperty = {
  id: InternalProperty.MediaLibraryV2Multi,
  pool: PropertyPool.Internal,
  type: PropertyType.MultipleChoice,
  name: "Media library",
  unifiedMediaLibraries: true,
};
let sources: ManagementSource[];
let sourcesLoading: boolean;

function Wrapper({ children }: PropsWithChildren) {
  return (
    <ResourceSourcesProvider value={{ sources, loading: sourcesLoading, refresh: () => {} }}>
      {children}
    </ResourceSourcesProvider>
  );
}

async function runRequests(delay = 0) {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(delay);
  });
}

beforeEach(() => {
  vi.useFakeTimers();
  request.mockReset();
  sources = [local, peer];
  sourcesLoading = false;
});
afterEach(() => vi.useRealTimers());

describe("unified media library resource counts", () => {
  it("compiles restored local-owner criteria even when this is the only source", async () => {
    sources = [local];
    const search = normalizeQueryOwner({
      owner: { serverId: local.serverId, libraryEpoch: local.libraryEpoch },
      page: 1,
      pageSize: 50,
      group: {
        combinator: GroupCombinator.And,
        disabled: false,
        filters: [
          {
            propertyId: property.id,
            propertyPool: property.pool,
            operation: SearchOperation.Contains,
            dbValue: "1",
            disabled: false,
          },
        ],
      },
    });

    request.mockResolvedValue({ data: { isReady: true, counts: { "1": 4 } } });
    const { result } = renderHook(
      () => useReferenceValueResourceCounts({ ...property, unifiedMediaLibraries: false }, search),
      { wrapper: Wrapper },
    );

    await runRequests(1000);
    expect(request).toHaveBeenCalledTimes(1);
    expect(request.mock.calls[0][0]).toEqual(local);
    const body = request.mock.calls[0][1].body;

    expect(body.group.filters).toEqual([
      {
        propertyId: property.id,
        propertyPool: property.pool,
        operation: SearchOperation.Contains,
        dbValue: "1",
        disabled: false,
      },
    ]);
    expect(JSON.stringify(body)).not.toContain('"owner"');
    expect(result.current).toMatchObject({ counts: { "1": 4 }, loading: false, error: false });
  });

  it.each([
    { ...local, serverId: "different-device" },
    { ...local, libraryEpoch: "previous-library" },
  ])(
    "does not execute criteria for another source identity against the local-only API ($serverId/$libraryEpoch)",
    async (owner) => {
      sources = [local];
      const search = { owner, page: 1, pageSize: 50 };
      const { result } = renderHook(
        () =>
          useReferenceValueResourceCounts({ ...property, unifiedMediaLibraries: false }, search),
        { wrapper: Wrapper },
      );

      await runRequests(1000);
      expect(request).not.toHaveBeenCalled();
      expect(result.current).toMatchObject({ counts: {}, loading: false, error: false });
    },
  );

  it.each([
    { owner: { serverId: local.serverId, libraryEpoch: local.libraryEpoch } },
    { deviceIds: [] },
    {
      group: {
        combinator: GroupCombinator.And,
        disabled: false,
        groups: [
          {
            combinator: GroupCombinator.And,
            disabled: false,
            filters: [
              {
                owner: { serverId: local.serverId, libraryEpoch: local.libraryEpoch },
                propertyId: 1,
                propertyPool: PropertyPool.Custom,
                disabled: false,
              },
            ],
          },
        ],
      },
    },
    {
      group: {
        combinator: GroupCombinator.And,
        disabled: false,
        filters: [
          {
            propertyId: property.id,
            propertyPool: property.pool,
            operation: SearchOperation.Contains,
            dbValue: "bakabase:library:malformed",
            disabled: false,
          },
        ],
      },
    },
  ])("does not send scoped criteria without a verified source (%j)", async (criteria) => {
    const { result } = renderHook(() =>
      useReferenceValueResourceCounts(
        { ...property, unifiedMediaLibraries: false },
        { ...criteria, page: 1, pageSize: 50 },
      ),
    );

    await runRequests(1000);
    expect(request).not.toHaveBeenCalled();
    expect(result.current.counts).toBeUndefined();
    expect(result.current).toMatchObject({ loading: false, error: true });
  });

  it("waits for source identity without showing successful zero counts and recovers when it arrives", async () => {
    sources = [];
    sourcesLoading = true;
    const search = {
      owner: { serverId: local.serverId, libraryEpoch: local.libraryEpoch },
      page: 1,
      pageSize: 50,
    };
    const { result, rerender } = renderHook(
      () => useReferenceValueResourceCounts({ ...property, unifiedMediaLibraries: false }, search),
      { wrapper: Wrapper },
    );

    await runRequests(2000);
    expect(request).not.toHaveBeenCalled();
    expect(result.current).toMatchObject({ loading: true, error: false });
    expect(result.current.counts).toBeUndefined();
    sources = [local];
    sourcesLoading = false;
    request.mockResolvedValue({ data: { isReady: true, counts: { "1": 2 } } });
    rerender();
    await runRequests(1000);
    expect(result.current).toMatchObject({ counts: { "1": 2 }, loading: false, error: false });
    expect(request).toHaveBeenCalledTimes(1);
    expect(request.mock.calls[0][0]).toEqual(local);
  });

  it("keeps equal library IDs separate by device and library epoch", async () => {
    const restored = { ...peer, libraryEpoch: "restored-epoch" };

    sources = [local, peer, restored];
    request.mockImplementation(async (source: ManagementSource) => ({
      data: {
        isReady: true,
        counts: { "1": source.isLocal ? 2 : source.libraryEpoch === peer.libraryEpoch ? 7 : 3 },
      },
    }));
    const { result } = renderHook(() => useReferenceValueResourceCounts(property), {
      wrapper: Wrapper,
    });

    await runRequests();
    expect(result.current.counts).toEqual({
      [mediaLibraryChoiceValue(local, 1)]: 2,
      [mediaLibraryChoiceValue(peer, 1)]: 7,
      [mediaLibraryChoiceValue(restored, 1)]: 3,
    });
    expect(result.current).toMatchObject({ loading: false, error: false });
  });

  it("waits for source discovery before settling global aggregate counts", async () => {
    sources = [];
    sourcesLoading = true;
    const { result, rerender } = renderHook(() => useReferenceValueResourceCounts(property), {
      wrapper: Wrapper,
    });

    await runRequests();
    expect(result.current.counts).toBeUndefined();
    expect(result.current).toMatchObject({ loading: true, error: false });
    expect(request).not.toHaveBeenCalled();
    sources = [local];
    sourcesLoading = false;
    request.mockResolvedValue({ data: { isReady: true, counts: { "1": 2 } } });
    rerender();
    await runRequests();
    expect(result.current.counts).toEqual({ [mediaLibraryChoiceValue(local, 1)]: 2 });
    expect(result.current.loading).toBe(false);
  });

  it("compiles selected library choices to owner-local IDs before requesting counts", async () => {
    const search: SearchForm = {
      page: 1,
      pageSize: 50,
      keyword: "selected",
      group: {
        combinator: GroupCombinator.And,
        disabled: false,
        filters: [
          {
            propertyId: property.id,
            propertyPool: property.pool,
            operation: SearchOperation.Contains,
            dbValue: serializeStandardValue(
              [mediaLibraryChoiceValue(peer, 1)],
              StandardValueType.ListString,
            ),
            disabled: false,
          },
        ],
      },
    };

    request.mockResolvedValue({ data: { isReady: true, counts: { "1": 4 } } });
    const { result } = renderHook(() => useReferenceValueResourceCounts(property, search), {
      wrapper: Wrapper,
    });

    await runRequests(1000);
    expect(request).toHaveBeenCalledTimes(1);
    expect(request.mock.calls[0][0]).toEqual(peer);
    expect(request.mock.calls[0][1].body).toMatchObject({
      keyword: "selected",
      group: {
        filters: [{ propertyId: property.id, operation: SearchOperation.Contains, dbValue: "1" }],
      },
    });
    expect(result.current.counts).toEqual({ [mediaLibraryChoiceValue(peer, 1)]: 4 });
  });

  it("includes completed offline replicas but skips unavailable and denied catalogs", async () => {
    const offline: ManagementSource = {
      ...peer,
      available: false,
      metadata: {
        identity: { serverId: peer.serverId, libraryEpoch: peer.libraryEpoch },
        name: peer.name,
        data: {
          activeGeneration: "complete-generation",
          resourceCount: 3,
          activeSequence: 12,
          stagedResourceCount: 1,
        },
        sync: { paused: false, isOffline: true },
        denied: false,
      },
    };

    sources = [
      local,
      offline,
      { ...peer, serverId: "unavailable", available: false },
      {
        ...offline,
        serverId: "denied",
        metadata: {
          ...offline.metadata!,
          identity: { serverId: "denied", libraryEpoch: peer.libraryEpoch },
          denied: true,
        },
      },
      {
        ...offline,
        serverId: "incomplete",
        metadata: {
          ...offline.metadata!,
          identity: { serverId: "incomplete", libraryEpoch: peer.libraryEpoch },
          data: { ...offline.metadata!.data, activeGeneration: undefined },
        },
      },
    ];
    request.mockResolvedValue({ data: { isReady: true, counts: { "1": 3 } } });
    const { result } = renderHook(() => useReferenceValueResourceCounts(property), {
      wrapper: Wrapper,
    });

    await runRequests();
    expect(request.mock.calls.map(([source]) => source.serverId)).toEqual(["local", "peer"]);
    expect(result.current).toMatchObject({ error: false, loading: false });
    expect(result.current.counts).toEqual({
      [mediaLibraryChoiceValue(local, 1)]: 3,
      [mediaLibraryChoiceValue(peer, 1)]: 3,
    });
  });

  it("does not reuse unqualified counts after switching to the aggregate property", async () => {
    request.mockResolvedValue({ data: { isReady: true, counts: { "1": 2 } } });
    const { result, rerender } = renderHook(
      ({ unified }) =>
        useReferenceValueResourceCounts({ ...property, unifiedMediaLibraries: unified }),
      { wrapper: Wrapper, initialProps: { unified: false } },
    );

    await runRequests();
    expect(result.current.counts).toEqual({ "1": 4 });
    rerender({ unified: true });
    expect(result.current.counts).toBeUndefined();
    expect(result.current.loading).toBe(true);
    await runRequests();
    expect(result.current.counts).toEqual({
      [mediaLibraryChoiceValue(local, 1)]: 2,
      [mediaLibraryChoiceValue(peer, 1)]: 2,
    });
  });

  it("keeps counts stable when only device names change", async () => {
    request.mockResolvedValue({ data: { isReady: true, counts: { "1": 2 } } });
    const { result, rerender } = renderHook(() => useReferenceValueResourceCounts(property), {
      wrapper: Wrapper,
    });

    await runRequests();
    const counts = result.current.counts;

    sources = sources.map((source) => ({ ...source, name: `${source.name} renamed` }));
    rerender();
    await runRequests();
    expect(result.current.counts).toBe(counts);
    expect(request).toHaveBeenCalledTimes(2);
  });

  it("keeps ordinary owner-scoped library counts in that owner's ID space", async () => {
    request.mockResolvedValue({ data: { isReady: true, counts: { "1": 7 } } });
    const { result } = renderHook(
      () => useReferenceValueResourceCounts({ ...property, unifiedMediaLibraries: false }),
      {
        wrapper: ({ children }) => (
          <Wrapper>
            <ResourceOwnerProvider source={peer}>{children}</ResourceOwnerProvider>
          </Wrapper>
        ),
      },
    );

    await runRequests();
    expect(request).toHaveBeenCalledTimes(1);
    expect(request.mock.calls[0][0]).toEqual(peer);
    expect(result.current.counts).toEqual({ "1": 7 });
  });

  it("retains qualified counts as stale if one readable replica fails during refresh", async () => {
    request.mockResolvedValue({ data: { isReady: true, counts: { "1": 2 } } });
    const { result } = renderHook(() => useReferenceValueResourceCounts(property), {
      wrapper: Wrapper,
    });

    await runRequests();
    const counts = result.current.counts;

    request.mockImplementation(async (source: ManagementSource) => {
      if (!source.isLocal) throw new Error("replica read failed");

      return { data: { isReady: true, counts: { "1": 4 } } };
    });
    act(() => result.current.refresh());
    await runRequests();
    expect(result.current.counts).toBe(counts);
    expect(result.current).toMatchObject({ error: true, loading: false, stale: true });
  });

  it("limits concurrent device requests while counting a large device list", async () => {
    sources = Array.from({ length: 6 }, (_, index) => ({
      ...peer,
      serverId: `peer-${index}`,
    }));
    let active = 0;
    let maximumActive = 0;
    const resolveRequests: (() => void)[] = [];

    request.mockImplementation(
      () =>
        new Promise((resolve) => {
          active++;
          maximumActive = Math.max(maximumActive, active);
          resolveRequests.push(() => {
            active--;
            resolve({ data: { isReady: true, counts: { "1": 1 } } });
          });
        }),
    );
    const { result } = renderHook(() => useReferenceValueResourceCounts(property), {
      wrapper: Wrapper,
    });

    await runRequests();
    expect(request).toHaveBeenCalledTimes(4);
    await act(async () => resolveRequests.splice(0).forEach((resolve) => resolve()));
    expect(request).toHaveBeenCalledTimes(6);
    await act(async () => resolveRequests.splice(0).forEach((resolve) => resolve()));
    expect(maximumActive).toBe(4);
    expect(Object.keys(result.current.counts!)).toHaveLength(6);
    expect(result.current.loading).toBe(false);
  });
});
