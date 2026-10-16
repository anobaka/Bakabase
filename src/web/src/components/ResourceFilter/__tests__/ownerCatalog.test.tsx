import type { ManagementSource } from "@/features/management/api";

import { beforeEach, describe, expect, it, vi } from "vitest";

import { createDefaultFilterConfig } from "../presets/DefaultFilterPreset";
import { toFilterInputModel } from "../utils/toInputModel";

import { ResourceOwnerProvider } from "@/features/management/resourceContext";
import BApi from "@/sdk/BApi";
import {
  InternalProperty,
  PropertyPool,
  PropertyType,
  ResourceProperty,
  SearchOperation,
} from "@/sdk/constants";

const { request } = vi.hoisted(() => ({ request: vi.fn() }));

vi.mock("@/sdk/BApi", async () => {
  const { Api } = await import("@/sdk/Api");

  return { default: Object.assign(new Api(), { request }) };
});
vi.mock("@/components/PropertySelector", () => ({ default: () => null }));
vi.mock("../components/Filter/FilterValueRenderer", () => ({ default: () => null }));

const source = (serverId: string, isLocal = false): ManagementSource => ({
  serverId,
  isLocal,
  libraryEpoch: `epoch-${serverId}`,
  name: serverId,
  available: true,
  contractVersion: 1,
});
const local = source("local", true);
const remote = source("remote");
const property = { id: 7, pool: PropertyPool.Custom, type: PropertyType.Tags, name: "Tags" } as any;

beforeEach(() => {
  request.mockReset().mockImplementation(async ({ path, body }) => {
    if (path.startsWith("/property/pool") || body?.operation === "properties") {
      return {
        code: 0,
        data: [
          property,
          {
            ...property,
            pool: PropertyPool.Internal,
            id: ResourceProperty.MediaLibraryV2Multi,
            name: "Libraries",
          },
          { ...property, pool: PropertyPool.Internal, id: ResourceProperty.Source, name: "Source" },
          {
            ...property,
            pool: PropertyPool.Internal,
            id: InternalProperty.Resource,
            name: "Resource",
          },
        ],
      };
    }
    if (body?.operation === "getSearchOperationsForProperty")
      return { code: 0, data: [SearchOperation.In] };
    if (body?.operation === "getFilterValueProperty") return { code: 0, data: property };

    return { code: 0, data: [] };
  });
});

describe("mixed-device filters reuse the normal property picker", () => {
  it("retains owner-bound properties from a completed offline replica", async () => {
    const offline: ManagementSource = {
      ...remote,
      available: false,
      metadata: {
        identity: { serverId: remote.serverId, libraryEpoch: remote.libraryEpoch },
        name: remote.name,
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
    const createPortal = vi.fn();

    await createDefaultFilterConfig(createPortal, BApi, [
      local,
      offline,
    ]).renderers.openPropertySelector(undefined, vi.fn());
    const props = createPortal.mock.calls[0][1];

    expect(
      props.properties.some(
        (p: any) => p.pool === PropertyPool.Custom && p.owner?.serverId === "remote",
      ),
    ).toBe(true);
    expect(props.unavailableSources).toEqual([]);
    request.mockClear();
    await createDefaultFilterConfig(createPortal, BApi, [local, offline]).api.saveRecentFilter({
      propertyId: 7,
      propertyPool: PropertyPool.Custom,
      operation: SearchOperation.In,
      disabled: false,
      owner: { serverId: offline.serverId, libraryEpoch: offline.libraryEpoch },
    });
    expect(request).not.toHaveBeenCalled();
  });
  it("keeps same-number properties distinct and only shares non-owner builtins", async () => {
    const createPortal = vi.fn().mockReturnValue({ destroy: vi.fn() });
    const onSelect = vi.fn();
    const config = createDefaultFilterConfig(createPortal, BApi, [local, remote]);

    await config.renderers.openPropertySelector(undefined, onSelect);
    const props = createPortal.mock.calls[0][1];

    expect(
      props.properties.filter(
        (p: any) => p.id === ResourceProperty.Source && p.pool === PropertyPool.Internal,
      ),
    ).toHaveLength(1);
    const libraries = props.properties.filter(
      (p: any) => p.pool === PropertyPool.Internal && p.id === ResourceProperty.MediaLibraryV2Multi,
    );

    expect(libraries).toHaveLength(1);
    expect(libraries[0]).toMatchObject({ unifiedMediaLibraries: true });
    expect(libraries[0].owner).toBeUndefined();
    expect(
      props.properties
        .filter((p: any) => p.pool === PropertyPool.Custom)
        .map((p: any) => p.owner.serverId),
    ).toEqual(["local", "remote"]);
    expect(
      props.properties
        .filter((p: any) => p.pool === PropertyPool.Internal && p.id === InternalProperty.Resource)
        .map((p: any) => p.owner.serverId),
    ).toEqual(["local", "remote"]);
    const selected = props.properties.find(
      (p: any) => p.pool === PropertyPool.Custom && p.owner?.serverId === "remote",
    );

    await props.onSubmit([selected]);
    expect(onSelect).toHaveBeenCalledWith(selected, [SearchOperation.In]);
    expect(request.mock.calls.at(-1)?.[0]).toMatchObject({
      path: "/federation/local/management/remote/request",
      body: {
        operation: "getSearchOperationsForProperty",
        libraryEpoch: "epoch-remote",
        query: { propertyPool: [String(PropertyPool.Custom)], propertyId: ["7"] },
      },
    });
  });

  it("keeps available catalogs when another device fails and names the missing device", async () => {
    const load = request.getMockImplementation()!;

    request.mockImplementation(async (input) => {
      if (input.path.includes("/remote/")) throw new Error("Device disconnected");

      return load(input);
    });
    const createPortal = vi.fn();
    const config = createDefaultFilterConfig(createPortal, BApi, [local, remote]);

    await config.renderers.openPropertySelector(undefined, vi.fn());
    const props = createPortal.mock.calls[0][1];

    expect(props.properties.some((p: any) => p.owner?.serverId === "local")).toBe(true);
    expect(props.properties.some((p: any) => p.owner?.serverId === "remote")).toBe(false);
    expect(props.unavailableSources).toEqual([{ name: "remote", message: "Device disconnected" }]);
  });

  it("limits catalog fanout to four owners", async () => {
    let inFlight = 0;
    let maximum = 0;

    request.mockImplementation(async () => {
      inFlight++;
      maximum = Math.max(maximum, inFlight);
      await new Promise((resolve) => setTimeout(resolve, 0));
      inFlight--;

      return { code: 0, data: [property] };
    });
    const createPortal = vi.fn();
    const config = createDefaultFilterConfig(
      createPortal,
      BApi,
      Array.from({ length: 9 }, (_, i) => source(`remote-${i}`)),
    );

    await config.renderers.openPropertySelector(undefined, vi.fn());
    expect(maximum).toBe(4);
    expect(createPortal.mock.calls[0][1].properties).toHaveLength(9);
  });

  it("hydrates remote filter values on the matching owner and scopes nested editors", async () => {
    const config = createDefaultFilterConfig(vi.fn(), BApi, [local, remote]);
    const valueProperty = await config.api.getValueProperty({
      owner: remote,
      propertyPool: PropertyPool.Custom,
      propertyId: 7,
      operation: SearchOperation.In,
      disabled: false,
    });

    expect(valueProperty?.owner).toEqual(remote);
    const rendered = config.renderers.renderValueInput(
      valueProperty!,
      undefined,
      undefined,
      vi.fn(),
    ) as any;

    expect(rendered.type).toBe(ResourceOwnerProvider);
    expect(rendered.props.source).toEqual(remote);
  });

  it.each([SearchOperation.In, SearchOperation.NotIn])(
    "loads the original remote property separately from operation %s's converted value property",
    async (operation) => {
      request.mockImplementation(async ({ body }) => ({
        code: 0,
        data:
          body?.operation === "properties"
            ? [{ ...property, type: PropertyType.SingleChoice }]
            : { ...property, type: PropertyType.MultipleChoice },
      }));
      const config = createDefaultFilterConfig(vi.fn(), BApi, [local, remote]);
      const filter = {
        owner: remote,
        propertyPool: PropertyPool.Custom,
        propertyId: 7,
        operation,
        disabled: false,
      };
      const [original, value] = await Promise.all([
        config.api.getProperty!(filter),
        config.api.getValueProperty(filter),
      ]);

      expect(original).toMatchObject({ type: PropertyType.SingleChoice, owner: remote });
      expect(value).toMatchObject({ type: PropertyType.MultipleChoice, owner: remote });
      expect(
        request.mock.calls.every(
          ([input]) => input.path === "/federation/local/management/remote/request",
        ),
      ).toBe(true);
    },
  );

  it("does not hydrate a deleted or replaced source through the viewer's same-number property", async () => {
    const config = createDefaultFilterConfig(vi.fn(), BApi, [local, remote]);

    await expect(
      config.api.getValueProperty({
        owner: { serverId: remote.serverId, libraryEpoch: "old" },
        propertyPool: PropertyPool.Custom,
        propertyId: 7,
        operation: SearchOperation.In,
        disabled: false,
      }),
    ).rejects.toThrow("identity has changed");
    expect(request).not.toHaveBeenCalled();
  });

  it("persists only owner identity alongside the original filter expression", () => {
    expect(
      toFilterInputModel({
        owner: remote,
        propertyPool: PropertyPool.Custom,
        propertyId: 7,
        dbValue: "choice",
      }),
    ).toEqual({
      owner: { serverId: "remote", libraryEpoch: "epoch-remote" },
      propertyPool: PropertyPool.Custom,
      propertyId: 7,
      operation: undefined,
      dbValue: "choice",
      disabled: false,
    });
  });
});
