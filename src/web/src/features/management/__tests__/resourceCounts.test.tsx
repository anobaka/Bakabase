import { act, renderHook } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import BApi from "@/sdk/BApi";
import { PropertyPool, PropertyType, ResourceProperty, SearchOperation } from "@/sdk/constants";
import { ResourceOwnerProvider } from "../resourceContext";
import { ResourceSourcesProvider } from "../resourceSourcesContext";
import { useReferenceValueResourceCounts } from "@/hooks/useReferenceValueResourceCounts";
import type { ManagementSource } from "../api";

vi.mock("@/sdk/BApi", async () => {
  const { Api } = await import("@/sdk/Api");

  return { default: Object.assign(new Api(), { request: vi.fn() }) };
});

const source = (serverId: string, isLocal = false): ManagementSource => ({
  serverId,
  isLocal,
  libraryEpoch: `epoch-${serverId}`,
  name: serverId,
  available: true,
  contractVersion: 1,
});
const property = { id: 87, pool: PropertyPool.Custom, type: PropertyType.Tags, name: "Tags" };

afterEach(() => vi.useRealTimers());

describe("owner-scoped filter option counts", () => {
  it("does not share in-flight same-number properties between devices", async () => {
    vi.useFakeTimers();
    vi.mocked(BApi.request)
      .mockReset()
      .mockImplementation(
        async ({ path }) =>
          ({
            code: 0,
            data: { isReady: true, counts: { choice: path.includes("/a/") ? 3 : 5 } },
          }) as any,
      );
    const a = source("a");
    const b = source("b");
    const first = renderHook(() => useReferenceValueResourceCounts(property), {
      wrapper: ({ children }) => (
        <ResourceOwnerProvider source={a}>{children}</ResourceOwnerProvider>
      ),
    });
    const second = renderHook(() => useReferenceValueResourceCounts(property), {
      wrapper: ({ children }) => (
        <ResourceOwnerProvider source={b}>{children}</ResourceOwnerProvider>
      ),
    });

    await act(async () => {
      await vi.advanceTimersByTimeAsync(0);
    });
    expect(BApi.request).toHaveBeenCalledTimes(2);
    expect(first.result.current.counts).toEqual({ choice: 3 });
    expect(second.result.current.counts).toEqual({ choice: 5 });
    first.unmount();
    second.unmount();
  });

  it("aggregates common choices with empty default filters and honors the device filter", async () => {
    vi.useFakeTimers();
    vi.mocked(BApi.request)
      .mockReset()
      .mockImplementation(
        async ({ path }) =>
          ({
            code: 0,
            data: { isReady: true, counts: { choice: path.includes("/b/") ? 5 : 3 } },
          }) as any,
      );
    const sources = { sources: [source("a", true), source("b")], loading: false, refresh: vi.fn() };
    const reference = {
      id: ResourceProperty.Source,
      pool: PropertyPool.Internal,
      type: PropertyType.MultipleChoice,
      name: "Source",
    };
    const hook = renderHook(
      ({ deviceIds }: { deviceIds?: string[] }) =>
        useReferenceValueResourceCounts(reference, {
          page: 1,
          pageSize: 50,
          deviceIds,
          group: {
            combinator: 1,
            disabled: false,
            filters: [
              {
                propertyPool: PropertyPool.Internal,
                propertyId: ResourceProperty.Source,
                operation: SearchOperation.In,
                disabled: false,
              },
              {
                owner: sources.sources[0],
                propertyPool: PropertyPool.Internal,
                propertyId: ResourceProperty.MediaLibraryV2Multi,
                operation: SearchOperation.In,
                dbValue: "",
                disabled: false,
              },
            ],
          },
        }),
      {
        initialProps: {} as { deviceIds?: string[] },
        wrapper: ({ children }) => (
          <ResourceSourcesProvider value={sources}>{children}</ResourceSourcesProvider>
        ),
      },
    );

    await act(async () => {
      await vi.advanceTimersByTimeAsync(1000);
    });
    expect(hook.result.current.counts).toEqual({ choice: 8 });
    hook.rerender({ deviceIds: ["b"] });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1000);
    });
    expect(hook.result.current.counts).toEqual({ choice: 5 });
    vi.mocked(BApi.request).mockClear();
    hook.rerender({ deviceIds: [] });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1000);
    });
    expect(hook.result.current.counts).toEqual({});
    expect(BApi.request).not.toHaveBeenCalled();
    hook.unmount();
  });
});
