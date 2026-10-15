import { describe, expect, it, vi } from "vitest";
import type { Resource } from "@/core/models/Resource";
import type { SearchForm } from "@/pages/resource/models";
import { GroupCombinator } from "@/components/ResourceFilter/models";
import {
  InternalProperty,
  PropertyPool,
  ResourceSearchSortableProperty,
  SearchOperation,
} from "@/sdk/constants";
import type { ManagementSource } from "../api";
import { ManagedResourcePager, compareOwnedResources } from "../resourceSearch";
import { compileResourceQuery, normalizeQueryOwner, resolveQuerySources } from "../resourceQuery";
import { resourceSelectionKey } from "../resourceIdentity";

const source = (serverId: string, isLocal = false): ManagementSource => ({
  serverId,
  libraryEpoch: `epoch-${serverId}`,
  name: serverId,
  isLocal,
  available: true,
  contractVersion: 1,
});
const a = source("a", true),
  b = source("b"),
  c = source("c");
const form: SearchForm = { page: 1, pageSize: 10 };
const filter = (owner?: ManagementSource) => ({
  owner,
  propertyId: 12,
  propertyPool: PropertyPool.Custom,
  operation: SearchOperation.Equals,
  dbValue: '"choice"',
  disabled: false,
});
const row = (id: number, extra: Partial<Resource> = {}) => ({ id, ...extra }) as Resource;

describe("unified resource query ownership", () => {
  it("searches a complete offline replica and reports incomplete replicas instead of empty success", async () => {
    const offline: ManagementSource = {
      ...b,
      available: false,
      metadata: {
        identity: { serverId: b.serverId, libraryEpoch: b.libraryEpoch },
        name: b.name,
        denied: false,
        data: {
          activeGeneration: "complete",
          resourceCount: 1,
          activeSequence: 1,
          stagedResourceCount: 0,
        },
        sync: { paused: false, isOffline: true },
      },
    };
    const request = vi.fn().mockResolvedValue({ data: [row(7)], totalCount: 1 });
    const pager = new ManagedResourcePager([offline], form, request, new AbortController().signal);
    expect((await pager.page(1)).data[0].owner).toEqual(offline);
    expect(pager.failures.size).toBe(0);
    const incomplete = {
      ...offline,
      available: true,
      metadata: {
        ...offline.metadata!,
        data: { ...offline.metadata!.data, activeGeneration: undefined },
      },
    };
    request.mockClear();
    const pending = new ManagedResourcePager(
      [incomplete],
      form,
      request,
      new AbortController().signal,
    );
    expect((await pending.page(1)).data).toEqual([]);
    expect(pending.failures.get(b.serverId)?.message).toContain("not ready");
    expect(request).not.toHaveBeenCalled();
  });
  it("treats backend null DeviceIds as all sources while keeping [] as none", () => {
    const restored = JSON.parse(
      '{"page":1,"pageSize":50,"owner":null,"deviceIds":null,"group":null,"orders":null,"keyword":null,"tags":null}',
    ) as SearchForm;
    expect(resolveQuerySources(restored, [a, b])).toEqual([a, b]);
    expect(compileResourceQuery(restored, a)).not.toBe(false);
    expect(compileResourceQuery(restored, b)).not.toBe(false);
    const normalized = normalizeQueryOwner(restored);
    expect(normalized.deviceIds).toBeUndefined();
    expect(restored.deviceIds).toBeNull();
    expect(resolveQuerySources({ ...restored, deviceIds: [] }, [a, b])).toEqual([]);
    expect(compileResourceQuery({ ...restored, deviceIds: [] }, b)).toBe(false);
    expect(normalizeQueryOwner({ ...restored, deviceIds: [] }).deviceIds).toEqual([]);
  });

  it("restores nullable group collections and order lists without throwing", () => {
    const restored = JSON.parse(
      '{"page":1,"pageSize":50,"deviceIds":null,"group":{"combinator":1,"disabled":false,"filters":null,"groups":[{"combinator":2,"disabled":false,"filters":null,"groups":null}]},"orders":null}',
    ) as SearchForm;
    expect(compileResourceQuery(restored, b)).toMatchObject({ group: undefined });
    expect(
      normalizeQueryOwner({ ...restored, owner: b }).group?.groups?.[0].filters,
    ).toBeUndefined();
    expect(compareOwnedResources(row(1), row(2), restored.orders)).toBeGreaterThan(0);
  });

  it("does not apply old local custom property IDs to another device", () => {
    const query = {
      ...form,
      group: { combinator: GroupCombinator.And, disabled: false, filters: [filter()] },
    };
    expect(compileResourceQuery(query, a)).not.toBe(false);
    expect(compileResourceQuery(query, b)).toBe(false);
  });
  it.each([undefined, null, "", " \t\n"])(
    "ignores empty owned/default filters (%s) before resolving device ownership",
    (dbValue) => {
      const query = {
        ...form,
        group: {
          combinator: GroupCombinator.And,
          disabled: false,
          filters: [
            { ...filter(a), dbValue: dbValue as string | undefined },
            {
              ...filter(),
              propertyPool: PropertyPool.Internal,
              propertyId: InternalProperty.MediaLibraryV2Multi,
              operation: SearchOperation.In,
              dbValue: dbValue as string | undefined,
            },
          ],
        },
      };
      expect(compileResourceQuery(query, a)).toMatchObject({ group: undefined });
      expect(compileResourceQuery(query, b)).toMatchObject({ group: undefined });
    },
  );
  it("ignores an empty nested OR branch without broadening a real owner-scoped predicate", () => {
    const query = {
      ...form,
      group: {
        combinator: GroupCombinator.Or,
        disabled: false,
        filters: [filter(b)],
        groups: [
          {
            combinator: GroupCombinator.And,
            disabled: false,
            filters: [{ ...filter(a), dbValue: undefined }],
          },
        ],
      },
    };
    expect(compileResourceQuery(query, a)).toBe(false);
    expect(compileResourceQuery(query, b)).toMatchObject({
      group: { filters: [{ dbValue: '"choice"' }], groups: [] },
    });
  });
  it.each([SearchOperation.IsNull, SearchOperation.IsNotNull])(
    "keeps valueless operation %s scoped to its actual owner",
    (operation) => {
      const query = {
        ...form,
        group: {
          combinator: GroupCombinator.And,
          disabled: false,
          filters: [{ ...filter(b), operation, dbValue: undefined }],
        },
      };
      expect(compileResourceQuery(query, a)).toBe(false);
      expect(compileResourceQuery(query, b)).toMatchObject({
        group: { filters: [{ operation }] },
      });
    },
  );
  it.each(["0", "False", "null", "[]"])("keeps meaningful serialized value %s", (dbValue) => {
    // StandardValue text is not JSON; even literal "null"/"[]" can be searched.
    const query = {
      ...form,
      group: {
        combinator: GroupCombinator.And,
        disabled: false,
        filters: [{ ...filter(b), dbValue }],
      },
    };
    expect(compileResourceQuery(query, a)).toBe(false);
    expect(compileResourceQuery(query, b)).toMatchObject({ group: { filters: [{ dbValue }] } });
  });
  it.each([
    InternalProperty.MediaLibraryV2,
    InternalProperty.MediaLibraryV2Multi,
    InternalProperty.CollectionMulti,
    InternalProperty.Resource,
    InternalProperty.ParentResource,
  ])("owner-binds reference property %s", (propertyId) => {
    expect(
      compileResourceQuery(
        {
          ...form,
          group: {
            combinator: 1,
            disabled: false,
            filters: [{ ...filter(), propertyPool: PropertyPool.Internal, propertyId }],
          },
        },
        b,
      ),
    ).toBe(false);
  });
  it("shares filename criteria but strips owner metadata from actual requests", () => {
    const query = {
      ...form,
      owner: b,
      group: {
        combinator: 1,
        disabled: false,
        filters: [
          {
            ...filter(b),
            propertyPool: PropertyPool.Internal,
            propertyId: InternalProperty.Filename,
          },
        ],
      },
    };
    const compiled = compileResourceQuery(query, b);
    expect(compiled).not.toBe(false);
    expect(JSON.stringify(compiled)).not.toContain('"owner"');
    expect(compileResourceQuery(query, c)).toBe(false);
  });
  it("treats foreign predicates as false rather than broadening AND or breaking OR", () => {
    const group = {
      combinator: GroupCombinator.And,
      disabled: false,
      filters: [filter(a), filter(b)],
    };
    expect(compileResourceQuery({ ...form, group }, a)).toBe(false);
    const or = compileResourceQuery(
      { ...form, group: { ...group, combinator: GroupCombinator.Or } },
      b,
    );
    expect(or && or.group?.filters).toHaveLength(1);
    expect(
      compileResourceQuery({ ...form, group: { ...group, combinator: GroupCombinator.Or } }, c),
    ).toBe(false);
  });
  it("ignores disabled predicates/groups without turning an OR into match-all", () => {
    const query = {
      ...form,
      group: {
        combinator: GroupCombinator.Or,
        disabled: false,
        filters: [{ ...filter(a), disabled: true }, filter(b)],
        groups: [{ combinator: 1, disabled: true, filters: [filter(a)] }],
      },
    };
    expect(compileResourceQuery(query, a)).toBe(false);
    expect(compileResourceQuery(query, b)).not.toBe(false);
  });
  it("never follows saved connectivity or a replaced library epoch", () => {
    const saved = { ...b, libraryEpoch: "old" };
    expect(resolveQuerySources({ ...form, owner: saved }, [a, b])[0].available).toBe(false);
    expect(resolveQuerySources({ ...form, deviceIds: [] }, [a, b])).toEqual([]);
    expect(compileResourceQuery({ ...form, owner: saved }, b)).toBe(false);
  });
});

describe("bounded multi-device search", () => {
  it("jumps to a deep single-owner page using only the required native page and reuses adjacent rows", async () => {
    const request = vi.fn(async (_source: ManagementSource, input: SearchForm) => ({
      totalCount: 100_000,
      data: Array.from({ length: 100 }, (_, index) => row((input.page - 1) * 100 + index + 1)),
    }));
    const pager = new ManagedResourcePager(
      [b],
      { ...form, pageSize: 50 },
      request,
      new AbortController().signal,
    );
    expect((await pager.page(1501)).data.map((resource) => resource.id)).toEqual(
      Array.from({ length: 50 }, (_, index) => 75_001 + index),
    );
    expect(request).toHaveBeenCalledTimes(1);
    expect(request.mock.calls[0][1]).toMatchObject({ page: 751, pageSize: 100 });
    expect((await pager.page(1502)).data[0].id).toBe(75_051);
    expect((await pager.page(1501)).data[0].id).toBe(75_001);
    expect(request).toHaveBeenCalledTimes(1);
  });

  it("loads at most the intersecting native pages when display pages cross native boundaries", async () => {
    const request = vi.fn(async (_source: ManagementSource, input: SearchForm) => ({
      totalCount: 100_000,
      data: Array.from({ length: 100 }, (_, index) => row((input.page - 1) * 100 + index + 1)),
    }));
    const pager = new ManagedResourcePager(
      [b],
      { ...form, pageSize: 75 },
      request,
      new AbortController().signal,
    );
    const result = await pager.page(1002);
    expect(result.data.map((resource) => resource.id)).toEqual(
      Array.from({ length: 75 }, (_, index) => 75_076 + index),
    );
    expect(request.mock.calls.map(([, page]) => page.page)).toEqual([751, 752]);
  });

  it("serializes overlapping mixed-page requests without duplicating initialization or consuming rows twice", async () => {
    const request = vi.fn(async (_source: ManagementSource) => {
      await new Promise((resolve) => setTimeout(resolve, 0));
      return { totalCount: 100, data: Array.from({ length: 100 }, (_, index) => row(100 - index)) };
    });
    const pager = new ManagedResourcePager(
      [a, b],
      { ...form, pageSize: 50 },
      request,
      new AbortController().signal,
    );
    const [first, second] = await Promise.all([pager.page(1), pager.page(2)]);
    const keys = [...first.data, ...second.data].map(resourceSelectionKey);
    expect(keys).toHaveLength(100);
    expect(new Set(keys).size).toBe(100);
    expect(first.data[0].id).toBe(100);
    expect(second.data[0].id).toBe(75);
    expect(request).toHaveBeenCalledTimes(2);
  });

  it("keeps coincident IDs separate and merges native default ordering", async () => {
    const request = vi.fn(async (owner: ManagementSource) => ({
      totalCount: 3,
      data: [row(1, { pinned: true }), row(3), row(2)],
    }));
    const pager = new ManagedResourcePager(
      [a, b],
      { ...form, pageSize: 3 },
      request,
      new AbortController().signal,
    );
    const first = await pager.page(1),
      second = await pager.page(2);
    expect([...first.data, ...second.data].map((item) => [item.owner?.serverId, item.id])).toEqual([
      ["a", 1],
      ["b", 1],
      ["a", 3],
      ["b", 3],
      ["a", 2],
      ["b", 2],
    ]);
    expect(new Set([...first.data, ...second.data].map(resourceSelectionKey)).size).toBe(6);
    expect(first.totalCount).toBe(6);
    expect(request).toHaveBeenCalledTimes(2);
  });
  it("reads only one bounded page per source to show a million-row library", async () => {
    const request = vi.fn(async (_source: ManagementSource, _form: SearchForm) => ({
      totalCount: 1_000_000,
      data: Array.from({ length: 100 }, (_, i) => row(1_000_000 - i)),
    }));
    const pager = new ManagedResourcePager([a, b], form, request, new AbortController().signal);
    expect((await pager.page(1)).data).toHaveLength(10);
    expect(request).toHaveBeenCalledTimes(2);
    expect(request.mock.calls.every(([, page]) => (page as SearchForm).pageSize === 100)).toBe(
      true,
    );
    await pager.page(2);
    expect(request).toHaveBeenCalledTimes(2);
  });
  it("paginates, caches and revisits without duplicating resources", async () => {
    const request = vi.fn(async (_source: ManagementSource, input: SearchForm) => ({
      totalCount: 230,
      data: Array.from(
        { length: Math.max(0, Math.min(100, 230 - (input.page - 1) * 100)) },
        (_, index) => row(230 - (input.page - 1) * 100 - index),
      ),
    }));
    const pager = new ManagedResourcePager(
      [a, b],
      { ...form, pageSize: 50 },
      request,
      new AbortController().signal,
    );
    const all = [];
    for (let page = 1; page <= 10; page++) all.push(...(await pager.page(page)).data);
    expect(all).toHaveLength(460);
    expect(new Set(all.map(resourceSelectionKey)).size).toBe(460);
    const requests = request.mock.calls.length;
    expect((await pager.page(3)).data).toEqual(all.slice(100, 150));
    expect(request).toHaveBeenCalledTimes(requests);
  });
  it("keeps reachable results and reports failed owners explicitly", async () => {
    const pager = new ManagedResourcePager(
      [a, b],
      form,
      async (owner) => {
        if (!owner.isLocal) throw new Error("offline");
        return { data: [row(1)], totalCount: 1 };
      },
      new AbortController().signal,
    );
    expect(await pager.page(1)).toMatchObject({ totalCount: 1, data: [{ id: 1 }] });
    expect(pager.failures.get("b")?.message).toBe("offline");
  });
  it("does not report cancellation as a failed source or publish stale pages", async () => {
    const abort = new AbortController();
    const pager = new ManagedResourcePager(
      [a],
      form,
      async () => {
        abort.abort();
        return { data: [row(1)], totalCount: 1 };
      },
      abort.signal,
    );
    await expect(pager.page(1)).rejects.toMatchObject({ name: "AbortError" });
    expect(pager.failures.size).toBe(0);
  });
  it("matches native missing-value sorting in both directions", () => {
    for (const asc of [true, false])
      expect(
        compareOwnedResources(row(1), row(2, { path: "/a", fileCreatedAt: "2020-01-01" }), [
          { property: ResourceSearchSortableProperty.FileCreateDt, asc },
        ]),
      ).toBeGreaterThan(0);
  });
});
