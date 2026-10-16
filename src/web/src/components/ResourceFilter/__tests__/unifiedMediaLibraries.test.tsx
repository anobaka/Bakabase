import type { IProperty } from "@/components/Property/models";
import type { ManagementSource } from "@/features/management/api";
import type { SearchFilter, SearchFilterGroup } from "../models";

import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { useState } from "react";

import { createDefaultFilterConfig } from "../presets/DefaultFilterPreset";
import { FilterProvider } from "../context/FilterContext";
import Filter from "../components/Filter";
import FilterGroupWithContext from "../components/FilterGroupWithContext";
import UnifiedMediaLibraryFilterValue from "../components/Filter/UnifiedMediaLibraryFilterValue";

import { ResourceSourcesProvider } from "@/features/management/resourceSourcesContext";
import { ResourceOwnerProvider } from "@/features/management/resourceContext";
import { mediaLibraryChoiceValue } from "@/features/management/mediaLibraryFilter";
import {
  deserializeStandardValue,
  serializeStandardValue,
} from "@/components/StandardValue/helpers";
import BApi from "@/sdk/BApi";
import {
  PropertyPool,
  PropertyType,
  ResourceProperty,
  SearchOperation,
  StandardValueType,
} from "@/sdk/constants";

const state = vi.hoisted(() => ({ request: vi.fn(), changed: vi.fn() }));

vi.mock("@/sdk/BApi", async () => {
  const { Api } = await import("@/sdk/Api");

  return { default: Object.assign(new Api(), { request: state.request }) };
});
vi.mock("react-i18next", () => ({
  useTranslation: () => ({ t: (key: string, args?: any) => args?.name ?? args?.device ?? key }),
}));
vi.mock("@/i18n", () => ({ getEnumKey: (key: string) => key }));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: vi.fn() }),
}));
vi.mock("@/features/management/DeviceDot", () => ({
  default: ({ name }: any) => <span title={name}>device</span>,
}));
vi.mock("@/features/management/ownedCatalog", () => ({
  CatalogSourceStatus: ({ sources, errors, loading }: any) => (
    <div role="status">
      {loading ? "loading" : ""}
      {sources
        .filter((source: any) => errors.has(JSON.stringify([source.serverId, source.libraryEpoch])))
        .map((source: any) => source.name)
        .join(",")}
    </div>
  ),
}));
vi.mock("@/components/PropertySelector", () => ({ default: () => null }));
vi.mock("../components/Filter/PropertyField", () => ({
  default: ({ property }: any) => <span>{property?.name}</span>,
}));
vi.mock("../components/Filter/OperationSelector", () => ({
  default: ({ onSelect }: any) => (
    <button onClick={() => onSelect(SearchOperation.IsNull)}>Is empty</button>
  ),
}));
vi.mock("../components/Filter/DeleteButton", () => ({ default: () => null }));
vi.mock("../components/Filter/DisableButton", () => ({ default: () => null }));
vi.mock("../components/FilterGroup", () => ({
  default: ({ group, onChange }: any) => (
    <>
      {group.filters.map((filter: SearchFilter, index: number) => (
        <Filter
          key={index}
          filter={filter}
          onChange={(next) =>
            onChange({
              ...group,
              filters: group.filters.map((value: SearchFilter, i: number) =>
                i === index ? next : value,
              ),
            })
          }
        />
      ))}
    </>
  ),
}));
vi.mock("../components/Filter/FilterValueRenderer", () => ({
  default: (props: any) => {
    const selected: string[] =
      deserializeStandardValue(props.dbValue ?? null, StandardValueType.ListString) ?? [];

    return (
      <div data-testid={props.property.unifiedMediaLibraries ? "unified" : "native"}>
        {props.optionsDescription}
        {(props.property.options?.choices ?? []).map((choice: any) => (
          <button
            key={choice.value}
            aria-pressed={selected.includes(choice.value)}
            onClick={() =>
              props.onValueChange(
                serializeStandardValue([...selected, choice.value], StandardValueType.ListString),
              )
            }
          >
            {choice.label}
            {props.renderOptionExtra?.(choice)}
          </button>
        ))}
        <output>{JSON.stringify(selected)}</output>
      </div>
    );
  },
}));

const source = (serverId: string, isLocal = false): ManagementSource => ({
  serverId,
  libraryEpoch: `epoch-${serverId}`,
  name: serverId,
  isLocal,
  available: true,
  contractVersion: 1,
});
const local = source("local", true),
  remote = source("remote");
const property = (name = "Local library"): IProperty => ({
  id: ResourceProperty.MediaLibraryV2Multi,
  pool: PropertyPool.Internal,
  type: PropertyType.MultipleChoice,
  name: "Libraries",
  dbValueType: StandardValueType.ListString,
  bizValueType: StandardValueType.ListString,
  typeName: "",
  poolName: "",
  order: 0,
  options: { choices: [{ value: "1", label: name }] },
});
const filter = (): SearchFilter => ({
  propertyPool: PropertyPool.Internal,
  propertyId: ResourceProperty.MediaLibraryV2Multi,
  operation: SearchOperation.In,
  disabled: false,
});
const value = (...ids: string[]) => serializeStandardValue(ids, StandardValueType.ListString)!;
const sourcesValue = (sources: ManagementSource[]) => ({
  sources,
  loading: false,
  refresh: vi.fn(),
});

function Existing({
  sources,
  initial = filter(),
  owner,
}: {
  sources: ManagementSource[];
  initial?: SearchFilter;
  owner?: ManagementSource;
}) {
  const [current, setCurrent] = useState(initial);
  const config = createDefaultFilterConfig(vi.fn(), BApi, sources, owner);

  return (
    <ResourceSourcesProvider value={sourcesValue(sources)}>
      <ResourceOwnerProvider source={owner}>
        <FilterProvider config={config}>
          <Filter
            filter={current}
            onChange={(next) => {
              state.changed(next);
              setCurrent(next);
            }}
          />
        </FilterProvider>
      </ResourceOwnerProvider>
    </ResourceSourcesProvider>
  );
}
function Defaults({ sources }: { sources: ManagementSource[] }) {
  const [group, setGroup] = useState<SearchFilterGroup>();

  return (
    <ResourceSourcesProvider value={sourcesValue(sources)}>
      <FilterGroupWithContext
        autoCreateMediaLibraryFilter
        group={group}
        onChange={(next) => {
          state.changed(next);
          setGroup(next);
        }}
      />
    </ResourceSourcesProvider>
  );
}
function replica(source: ManagementSource, revision = 1): ManagementSource {
  return {
    ...source,
    metadata: {
      identity: { serverId: source.serverId, libraryEpoch: source.libraryEpoch },
      name: source.name,
      denied: false,
      data: {
        activeGeneration: "active",
        activeSequence: 1,
        resourceCount: 1,
        stagedResourceCount: 0,
        checkpoint: {
          generation: "active",
          complete: true,
          sequence: 1,
          catalogRevision: revision,
        },
      },
      sync: { paused: false, isOffline: !source.available },
    },
  };
}
beforeEach(() => {
  state.changed.mockReset();
  state.request.mockReset().mockImplementation(async ({ path, body }: any) => {
    const item = property(path.includes("/remote/") ? "Remote library" : "Local library");

    if (path.startsWith("/property/pool") || body?.operation === "properties")
      return { code: 0, data: [item] };
    if (path.includes("filter-value-property") || body?.operation === "getFilterValueProperty")
      return { code: 0, data: item };

    return { code: 0, data: [SearchOperation.In] };
  });
});
afterEach(cleanup);

describe("unified ordinary media-library filter", () => {
  it("auto-created default has one picker and writes owner-qualified choices", async () => {
    render(<Defaults sources={[local, remote]} />);
    await screen.findByRole("button", { name: "Remote librarydevice" });
    expect(screen.getAllByTestId("unified")).toHaveLength(1);
    expect(screen.getByRole("button", { name: "Local library" })).toBeTruthy();
    expect(screen.getByTitle("remote")).toBeTruthy();
    state.changed.mockClear();
    fireEvent.click(screen.getByRole("button", { name: "Remote librarydevice" }));
    const saved = state.changed.mock.calls.at(-1)![0].filters[0];

    expect(saved.owner).toBeUndefined();
    expect(saved.dbValue).toBe(value(mediaLibraryChoiceValue(remote, 1)));
  });
  it("upgrades a populated legacy filter when sources arrive without changing it until an edit", async () => {
    const initial = {
      ...filter(),
      owner: local,
      dbValue: value("1"),
      property: property(),
      valueProperty: property(),
    };
    const view = render(<Existing initial={initial} sources={[local]} />);

    expect(screen.getByTestId("native")).toBeTruthy();
    view.rerender(<Existing initial={initial} sources={[local, remote]} />);
    await screen.findByRole("button", { name: "Remote librarydevice" });
    expect(screen.getByRole("button", { name: "Local library" }).getAttribute("aria-pressed")).toBe(
      "true",
    );
    expect(
      screen.getByRole("button", { name: "Remote librarydevice" }).getAttribute("aria-pressed"),
    ).toBe("false");
    expect(state.changed).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "Remote librarydevice" }));
    const saved = state.changed.mock.calls.at(-1)![0];

    expect(saved.owner).toBeUndefined();
    expect(saved.dbValue).toBe(
      value(mediaLibraryChoiceValue(local, 1), mediaLibraryChoiceValue(remote, 1)),
    );
  });
  it("keeps an explicitly owner-scoped view's native numeric choices", async () => {
    render(
      <Existing
        initial={{ ...filter(), owner: remote }}
        owner={remote}
        sources={[local, remote]}
      />,
    );
    await screen.findByRole("button", { name: "Remote library" });
    expect(screen.queryByTestId("unified")).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Remote library" }));
    expect(state.changed.mock.calls.at(-1)![0]).toMatchObject({
      owner: remote,
      dbValue: value("1"),
    });
  });
  it("makes an explicitly changed empty-library operation apply across owners", async () => {
    render(
      <Existing
        initial={{ ...filter(), owner: local, dbValue: value("1") }}
        sources={[local, remote]}
      />,
    );
    await screen.findByRole("button", { name: "Remote librarydevice" });
    fireEvent.click(screen.getByRole("button", { name: "Is empty" }));
    await waitFor(() => expect(state.changed).toHaveBeenCalled());
    expect(state.changed.mock.calls.at(-1)![0]).toMatchObject({
      owner: undefined,
      dbValue: undefined,
      operation: SearchOperation.IsNull,
    });
  });
  it("refreshes renamed catalogs without changing the saved selection and reads offline replicas", async () => {
    const initial = { ...filter(), dbValue: value(mediaLibraryChoiceValue(remote, 1)) };
    const view = render(
      <Existing initial={initial} sources={[local, replica({ ...remote, available: false })]} />,
    );

    await screen.findByRole("button", { name: "Remote librarydevice" });
    const load = state.request.getMockImplementation()!;

    state.request.mockImplementation(async (input) =>
      input.body?.operation === "getFilterValueProperty"
        ? { code: 0, data: property("Renamed library") }
        : load(input),
    );
    view.rerender(
      <Existing initial={initial} sources={[local, replica({ ...remote, available: false }, 2)]} />,
    );
    const renamed = await screen.findByRole("button", { name: "Renamed librarydevice" });

    expect(renamed.getAttribute("aria-pressed")).toBe("true");
    expect(state.changed).not.toHaveBeenCalled();
  });
  it("retains a removed device's selection without rebinding its ID to a local library", async () => {
    render(
      <Existing
        initial={{
          ...filter(),
          dbValue: value(mediaLibraryChoiceValue(remote, 1)),
          bizValue: value("Remote library"),
        }}
        sources={[local]}
      />,
    );
    await screen.findByRole("button", { name: "Local library" });
    expect(screen.getByRole("button", { name: "Local library" }).getAttribute("aria-pressed")).toBe(
      "false",
    );
    expect(
      screen.getByRole("button", { name: "Remote librarydevice" }).getAttribute("aria-pressed"),
    ).toBe("true");
    expect(state.changed).not.toHaveBeenCalled();
  });
  it("still hydrates old numeric owner-bound filters after their source is removed", async () => {
    render(
      <Existing
        initial={{
          ...filter(),
          owner: remote,
          dbValue: value("1"),
          bizValue: value("Remote library"),
        }}
        sources={[local]}
      />,
    );
    await screen.findByRole("button", { name: "Remote librarydevice" });
    expect(screen.getByRole("button", { name: "Local library" }).getAttribute("aria-pressed")).toBe(
      "false",
    );
    expect(state.changed).not.toHaveBeenCalled();
  });
  it("excludes revoked catalogs and reports failed readable sources", async () => {
    const denied = replica(source("denied"));

    denied.metadata!.denied = true;
    const load = state.request.getMockImplementation()!;

    state.request.mockImplementation(async (input) => {
      if (input.path.includes("/remote/")) throw new Error("Unavailable");

      return load(input);
    });
    render(
      <UnifiedMediaLibraryFilterValue property={property()} sources={[local, remote, denied]} />,
    );
    await waitFor(() => expect(screen.getByText("remote").textContent).toBe("remote"));
    expect(state.request.mock.calls.some(([input]) => input.path.includes("/denied/"))).toBe(false);
    expect(screen.getByRole("button", { name: "Local library" })).toBeTruthy();
  });

  it("does not borrow a same-number local label for an unavailable saved remote choice", async () => {
    render(
      <Existing
        initial={{ ...filter(), dbValue: value(mediaLibraryChoiceValue(remote, 1)) }}
        sources={[local]}
      />,
    );
    await screen.findByRole("button", { name: "Local library" });
    expect(screen.getByRole("button", { name: "#1device" }).getAttribute("aria-pressed")).toBe(
      "true",
    );
    expect(screen.getByRole("button", { name: "Local library" }).getAttribute("aria-pressed")).toBe(
      "false",
    );
  });

  it("discards a late catalog response from a replaced library epoch", async () => {
    let release: (value: unknown) => void = () => {};
    const oldCatalog = new Promise((resolve) => {
      release = resolve;
    });
    const load = state.request.getMockImplementation()!;

    state.request.mockImplementation(async (input) => {
      if (input.body?.libraryEpoch === remote.libraryEpoch) return oldCatalog;
      if (input.body?.libraryEpoch === "replacement")
        return { code: 0, data: property("Replacement library") };

      return load(input);
    });
    const view = render(
      <UnifiedMediaLibraryFilterValue property={property()} sources={[local, remote]} />,
    );

    await waitFor(() =>
      expect(
        state.request.mock.calls.some(
          ([input]) => input.body?.libraryEpoch === remote.libraryEpoch,
        ),
      ).toBe(true),
    );
    view.rerender(
      <UnifiedMediaLibraryFilterValue
        property={property()}
        sources={[local, { ...remote, libraryEpoch: "replacement" }]}
      />,
    );
    await screen.findByRole("button", { name: "Replacement librarydevice" });
    await act(async () => {
      release({ code: 0, data: property("Old library") });
    });
    expect(screen.queryByRole("button", { name: "Old librarydevice" })).toBeNull();
    expect(screen.getByRole("button", { name: "Replacement librarydevice" })).toBeTruthy();
  });
  it("bounds catalog fanout to four devices", async () => {
    let active = 0,
      maximum = 0;

    state.request.mockImplementation(async () => {
      active++;
      maximum = Math.max(maximum, active);
      await new Promise((resolve) => setTimeout(resolve, 0));
      active--;

      return { code: 0, data: property() };
    });
    render(
      <UnifiedMediaLibraryFilterValue
        property={property()}
        sources={Array.from({ length: 9 }, (_, index) => source(`device-${index}`))}
      />,
    );
    await waitFor(() => expect(screen.getAllByRole("button")).toHaveLength(9));
    expect(maximum).toBe(4);
  });
});
