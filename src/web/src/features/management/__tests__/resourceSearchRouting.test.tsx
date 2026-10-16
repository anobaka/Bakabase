import { act, cleanup, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { useResourceSearch } from "@/hooks/useResourceSearch";
import { PropertyPool, ResourceProperty, SearchOperation, StandardValueType } from "@/sdk/constants";
import { mediaLibraryChoiceValue } from "../mediaLibraryFilter";
import { serializeStandardValue } from "@/components/StandardValue/helpers";

const calls = vi.hoisted(() => ({
  managedSearch: vi.fn(),
  managedAll: vi.fn(),
  localSearch: vi.fn(),
  localAll: vi.fn(),
}));
vi.mock("../useManagedResourceSearch", () => ({
  useManagedResourceSearch: () => ({
    enabled: false,
    search: calls.managedSearch,
    searchAllResourceKeys: calls.managedAll,
    resources: [],
    loading: false,
    loadingDetails: false,
    response: undefined,
  }),
}));
vi.mock("@/sdk/BApi", () => ({
  default: {
    resource: {
      searchResources: calls.localSearch,
      searchAllResourceIds: calls.localAll,
    },
  },
}));

beforeEach(() => {
  calls.managedSearch.mockReset().mockResolvedValue([]);
  calls.managedAll.mockReset().mockResolvedValue([]);
  calls.localSearch
    .mockReset()
    .mockResolvedValue({ code: 0, data: [], pageIndex: 1, pageSize: 50, totalCount: 0 });
  calls.localAll.mockReset().mockResolvedValue({ code: 0, data: [] });
});
afterEach(cleanup);

const ownedFilter = {
  owner: { serverId: "former-device", libraryEpoch: "saved-epoch" },
  propertyPool: PropertyPool.Custom,
  propertyId: 7,
  operation: SearchOperation.Equals,
  dbValue: '"foreign-value"',
  disabled: false,
};

describe("resource search owner routing after devices are removed", () => {
  it("compiles saved unified library selections even after the last managed device is removed", async () => {
    const { result } = renderHook(useResourceSearch);
    const form = {
      page: 1,
      pageSize: 50,
      group: {
        combinator: 1 as const,
        disabled: false,
        groups: [{
          combinator: 2 as const,
          disabled: false,
          filters: [{
            propertyPool: PropertyPool.Internal,
            propertyId: ResourceProperty.MediaLibraryV2Multi,
            operation: SearchOperation.In,
            dbValue: serializeStandardValue(
              [mediaLibraryChoiceValue(ownedFilter.owner, "7")],
              StandardValueType.ListString,
            ),
            disabled: false,
          }],
        }],
      },
    };
    await act(() => result.current.search(form));
    await act(() => result.current.searchAllResourceKeys(form));
    expect(calls.managedSearch).toHaveBeenCalledWith(form, undefined);
    expect(calls.managedAll).toHaveBeenCalledWith(form);
    expect(calls.localSearch).not.toHaveBeenCalled();
    expect(calls.localAll).not.toHaveBeenCalled();
  });

  it("keeps an all-in-one saved query with backend null fields on the normal local endpoints", async () => {
    const { result } = renderHook(useResourceSearch);
    const form = JSON.parse(
      '{"page":1,"pageSize":50,"owner":null,"deviceIds":null,"group":null,"orders":null,"keyword":null,"tags":null}',
    );
    await act(() => result.current.search(form, { saveSearch: true, searchId: "local-tab" }));
    await act(() => result.current.searchAllResourceKeys(form));
    expect(calls.localSearch).toHaveBeenCalledWith(
      form,
      expect.objectContaining({ saveSearch: true, searchId: "local-tab" }),
    );
    expect(calls.localAll).toHaveBeenCalledWith(form);
    expect(calls.managedSearch).not.toHaveBeenCalled();
    expect(calls.managedAll).not.toHaveBeenCalled();
  });

  it("still routes an explicit empty DeviceIds list through the management path", async () => {
    const { result } = renderHook(useResourceSearch);
    const form = { page: 1, pageSize: 50, deviceIds: [] };
    await act(() => result.current.search(form));
    await act(() => result.current.searchAllResourceKeys(form));
    expect(calls.managedSearch).toHaveBeenCalledWith(form, undefined);
    expect(calls.managedAll).toHaveBeenCalledWith(form);
    expect(calls.localSearch).not.toHaveBeenCalled();
    expect(calls.localAll).not.toHaveBeenCalled();
  });

  it("keeps nested filter ownership away from the local search endpoint even without an enabled management context", async () => {
    const { result } = renderHook(useResourceSearch);
    const form = {
      page: 1,
      pageSize: 50,
      group: {
        combinator: 1 as const,
        disabled: false,
        groups: [{ combinator: 2 as const, disabled: false, filters: [ownedFilter] }],
      },
    };
    await act(() => result.current.search(form, { saveSearch: true, searchId: "saved-tab" }));
    expect(calls.managedSearch).toHaveBeenCalledWith(form, {
      saveSearch: true,
      searchId: "saved-tab",
    });
    expect(calls.localSearch).not.toHaveBeenCalled();
  });

  it("routes owner-qualified select-all safely even before a normal search has run", async () => {
    const { result } = renderHook(useResourceSearch);
    const form = {
      page: 1,
      pageSize: 50,
      group: {
        combinator: 1 as const,
        disabled: false,
        filters: [{ ...ownedFilter, disabled: true }],
      },
    };
    await act(() => result.current.searchAllResourceKeys(form));
    expect(calls.managedAll).toHaveBeenCalledWith(form);
    expect(calls.localAll).not.toHaveBeenCalled();
  });

  it("preserves the native all-in-one path for an ordinary local query", async () => {
    const { result } = renderHook(useResourceSearch);
    const form = { page: 1, pageSize: 50, keyword: "local" };
    await act(() => result.current.search(form));
    await act(() => result.current.searchAllResourceKeys(form));
    expect(calls.localSearch).toHaveBeenCalledTimes(1);
    expect(calls.localAll).toHaveBeenCalledWith(form);
    expect(calls.managedSearch).not.toHaveBeenCalled();
    expect(calls.managedAll).not.toHaveBeenCalled();
  });
});
