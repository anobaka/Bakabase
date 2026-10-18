import type * as Zustand from "zustand";
import type {
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsDomainResourceOptionsSavedSearch as SavedSearch,
  BakabaseServiceModelsInputResourceSearchInputModel as SearchInput,
} from "@/sdk/Api";

import { act, StrictMode } from "react";
import { createRoot, type Root } from "react-dom/client";
import { HeroUIProvider } from "@heroui/react";
import { fireEvent } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import ResourcePage from "../index";

import { dashboardResourceSearch } from "@/pages/dashboard/dashboardSearch";
import { deserializeStandardValue } from "@/components/StandardValue/helpers";
import { useResourceOptionsStore } from "@/stores/options";
import { usePendingSearchStore } from "@/stores/pendingSearch";
import {
  FilterDisplayMode,
  InternalProperty,
  PropertyPool,
  PropertyType,
  SearchCombinator,
  SearchOperation,
  StandardValueType,
} from "@/sdk/constants";

const mocks = vi.hoisted(() => ({
  save: vi.fn(),
  getSaved: vi.fn(),
  getAllSaved: vi.fn(),
  rename: vi.fn(),
  remove: vi.fn(),
  tabProps: new Map<string, any>(),
  libraryEpoch: "library",
}));

vi.mock("@/sdk/BApi", () => ({
  default: {
    resource: {
      saveNewResourceSearch: mocks.save,
      getSavedSearch: mocks.getSaved,
      getSavedSearches: mocks.getAllSaved,
      putSavedSearchName: mocks.rename,
      deleteSavedSearch: mocks.remove,
    },
  },
}));
vi.mock("@/features/management/useManagementSources", () => ({
  useManagementSources: () => ({
    sources: [{ isLocal: true, serverId: "this-server", libraryEpoch: mocks.libraryEpoch }],
    loading: false,
    refresh: vi.fn(),
  }),
}));
vi.mock("@/components/bakaui", async () => await vi.importActual("@heroui/react"));
vi.mock("@/stores/options", async () => {
  const { create } = await vi.importActual<typeof Zustand>("zustand");

  return {
    useResourceOptionsStore: create(() => ({ initialized: false, data: { savedSearches: [] } })),
  };
});
vi.mock("../components/ResourceTabContent", () => ({
  default: (props: any) => {
    mocks.tabProps.set(props.searchId, props);

    return <div data-active={String(props.activated)} data-search-id={props.searchId} />;
  },
}));
vi.mock("../components/RecentlyPlayedDrawer", () => ({ default: () => null }));
vi.mock("../components/SearchSummary", () => ({ default: () => null }));
vi.mock("@/components/ResourceMovePanel", () => ({
  default: () => null,
  ResourceMovePanelDock: () => null,
}));

let host: HTMLDivElement;
let root: Root;

function saved(id: string): SavedSearch {
  return { id, name: `Saved ${id}`, search: { page: 1, pageSize: 50 }, displayMode: 1 };
}

function resolvedSearch(id: string, label: string, name = "") {
  return {
    ...saved(id),
    name,
    search: {
      page: 1,
      pageSize: 50,
      group: {
        combinator: SearchCombinator.And,
        disabled: false,
        filters: [
          { disabled: false, bizValue: label, property: { type: PropertyType.SingleLineText } },
        ],
      },
    },
  };
}

function deferredSearches() {
  let resolve!: (value: { data: ReturnType<typeof resolvedSearch>[] }) => void;
  const promise = new Promise<{ data: ReturnType<typeof resolvedSearch>[] }>((res) => {
    resolve = res;
  });

  return { promise, resolve };
}

function options(initialized: boolean, savedSearches: SavedSearch[] = []) {
  useResourceOptionsStore.setState({
    initialized,
    data: { ...useResourceOptionsStore.getState().data, savedSearches },
  });
}

function incoming(): SearchInput {
  return {
    page: 1,
    pageSize: 100,
    group: {
      combinator: SearchCombinator.And,
      disabled: false,
      filters: [
        {
          propertyPool: PropertyPool.Internal,
          propertyId: InternalProperty.MediaLibraryV2Multi,
          operation: SearchOperation.In,
          dbValue: "7",
          disabled: false,
        },
      ],
    },
  };
}

function deferred() {
  let resolve!: (value: { data: { id: string; name: string } }) => void;
  const promise = new Promise<{ data: { id: string; name: string } }>((res) => {
    resolve = res;
  });

  return { promise, resolve };
}

async function render() {
  await act(async () =>
    root.render(
      <StrictMode>
        <HeroUIProvider disableAnimation>
          <ResourcePage />
        </HeroUIProvider>
      </StrictMode>,
    ),
  );
}

function activeSearch() {
  return host.querySelector('[data-active="true"]')?.getAttribute("data-search-id");
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.save.mockReset();
  mocks.getSaved.mockReset();
  mocks.getAllSaved.mockReset().mockResolvedValue({ data: [] });
  mocks.rename.mockReset().mockResolvedValue({ code: 0 });
  mocks.remove.mockReset().mockResolvedValue({ code: 0 });
  mocks.tabProps.clear();
  mocks.libraryEpoch = "library";
  vi.stubGlobal("IS_REACT_ACT_ENVIRONMENT", true);
  localStorage.clear();
  usePendingSearchStore.setState({ pendingSearch: undefined });
  options(false);
  mocks.getSaved.mockResolvedValue({ data: undefined });
  host = document.createElement("div");
  document.body.appendChild(host);
  root = createRoot(host);
});

afterEach(async () => {
  await act(async () => root.unmount());
  host.remove();
  localStorage.clear();
  vi.unstubAllGlobals();
});

describe("resource page incoming searches", () => {
  it("preserves dashboard filter dbValues in the actual save-search request payload", async () => {
    mocks.save.mockResolvedValueOnce({ data: { id: "filtered", name: "Local library resources" } });
    options(true);
    usePendingSearchStore
      .getState()
      .setPendingSearch(dashboardResourceSearch({ localOnly: true, libraryId: 7 }));
    await render();

    expect(mocks.save).toHaveBeenCalledTimes(1);
    const payload = JSON.parse(JSON.stringify(mocks.save.mock.calls[0][0]));

    expect(payload).toEqual({
      displayMode: FilterDisplayMode.Simple,
      search: {
        page: 1,
        pageSize: 100,
        group: {
          combinator: SearchCombinator.And,
          disabled: false,
          filters: [
            {
              propertyPool: PropertyPool.Internal,
              propertyId: InternalProperty.HasLocalPath,
              operation: SearchOperation.Equals,
              dbValue: "True",
              disabled: false,
            },
            {
              propertyPool: PropertyPool.Internal,
              propertyId: InternalProperty.MediaLibraryV2Multi,
              operation: SearchOperation.In,
              dbValue: "7",
              disabled: false,
            },
          ],
        },
      },
    });
    const [local, library] = payload.search.group.filters;

    expect(local).not.toHaveProperty("value");
    expect(library).not.toHaveProperty("value");
    expect(deserializeStandardValue(local.dbValue, StandardValueType.Boolean)).toBe(true);
    expect(deserializeStandardValue(library.dbValue, StandardValueType.ListString)).toEqual(["7"]);
    expect(activeSearch()).toBe("filtered");
  });

  it("creates only the incoming query when first opened without saved searches", async () => {
    const creation = deferred();

    mocks.save.mockReturnValueOnce(creation.promise);
    options(true);
    const query = incoming();

    usePendingSearchStore.getState().setPendingSearch(query);
    await render();

    expect(mocks.save).toHaveBeenCalledTimes(1);
    expect(mocks.save).toHaveBeenCalledWith({
      search: query,
      displayMode: FilterDisplayMode.Simple,
    });
    expect(usePendingSearchStore.getState().pendingSearch).toBeUndefined();
    await render();
    expect(mocks.save).toHaveBeenCalledTimes(1);
    await act(async () => creation.resolve({ data: { id: "incoming", name: "Library 7" } }));
    expect(activeSearch()).toBe("incoming");
    expect(localStorage.getItem("resource-active-tab-id")).toBe("incoming");
    expect(mocks.save).toHaveBeenCalledTimes(1);
  });

  it.each([false, true])(
    "waits for options before consuming a pending query (saved tabs: %s)",
    async (hasSaved) => {
      const query = incoming();
      const creation = deferred();

      mocks.save.mockReturnValueOnce(creation.promise);
      usePendingSearchStore.getState().setPendingSearch(query);
      localStorage.setItem("resource-active-tab-id", "restored");
      await render();
      expect(mocks.save).not.toHaveBeenCalled();
      expect(usePendingSearchStore.getState().pendingSearch).toEqual(query);

      await act(async () => options(true, hasSaved ? [saved("first"), saved("restored")] : []));
      expect(mocks.save).toHaveBeenCalledTimes(1);
      expect(mocks.save).toHaveBeenCalledWith({
        search: query,
        displayMode: FilterDisplayMode.Simple,
      });
      expect(usePendingSearchStore.getState().pendingSearch).toBeUndefined();
      if (hasSaved) expect(activeSearch()).toBe("restored");
      await act(async () => creation.resolve({ data: { id: "incoming", name: "Library 7" } }));
      expect(activeSearch()).toBe("incoming");
      if (hasSaved) {
        expect(host.textContent).toContain("Saved first");
        expect(host.textContent).toContain("Saved restored");
      }
      await act(async () => options(true, hasSaved ? [saved("first"), saved("restored")] : []));
      expect(activeSearch()).toBe("incoming");
      expect(mocks.save).toHaveBeenCalledTimes(1);
    },
  );

  it.each(["restored", "missing"])(
    "preserves saved-tab restoration without creating a search (previous: %s)",
    async (previous) => {
      localStorage.setItem("resource-active-tab-id", previous);
      options(true, [saved("first"), saved("restored")]);
      await render();
      expect(activeSearch()).toBe(previous === "restored" ? "restored" : "first");
      expect(mocks.save).not.toHaveBeenCalled();
    },
  );

  it("keeps a later incoming query pending while the initial default tab is saving", async () => {
    const initial = deferred();
    const next = deferred();

    mocks.save.mockReturnValueOnce(initial.promise).mockReturnValueOnce(next.promise);
    options(true);
    await render();
    expect(mocks.save).toHaveBeenCalledTimes(1);
    expect(mocks.save).toHaveBeenCalledWith({
      search: { page: 1, pageSize: 50 },
      displayMode: FilterDisplayMode.Simple,
    });
    const query = incoming();

    await act(async () => usePendingSearchStore.getState().setPendingSearch(query));
    expect(usePendingSearchStore.getState().pendingSearch).toEqual(query);
    expect(mocks.save).toHaveBeenCalledTimes(1);

    await act(async () => initial.resolve({ data: { id: "default", name: "All resources" } }));
    expect(mocks.save).toHaveBeenCalledTimes(2);
    expect(mocks.save).toHaveBeenLastCalledWith({
      search: query,
      displayMode: FilterDisplayMode.Simple,
    });
    expect(usePendingSearchStore.getState().pendingSearch).toBeUndefined();
    await act(async () => next.resolve({ data: { id: "incoming", name: "Library 7" } }));
    expect(activeSearch()).toBe("incoming");
    expect(host.textContent).toContain("All resources");
    await render();
    expect(mocks.save).toHaveBeenCalledTimes(2);
  });
});

describe("initial resource tab names", () => {
  it("loads every untouched tab's criteria in one batch while preserving custom names", async () => {
    options(true, [
      { ...saved("first"), name: "My pinned search" },
      { ...saved("second"), name: "" },
      { ...saved("third"), name: "" },
    ]);
    mocks.getAllSaved.mockResolvedValue({
      data: [
        resolvedSearch("first", "Filter name", "Old custom name"),
        resolvedSearch("second", "Pictures"),
        resolvedSearch("third", "Movies"),
      ],
    });
    await render();

    expect(host.textContent).toContain("My pinned search");
    expect(host.textContent).toContain("Pictures");
    expect(host.textContent).toContain("Movies");
    expect(host.querySelector('[data-search-id="second"]')).toBeNull();
    expect(host.querySelector('[data-search-id="third"]')).toBeNull();
    expect(mocks.getAllSaved).toHaveBeenCalledTimes(1);
    const untouchedLabel = [...host.querySelectorAll("span")].find(
      (element) => element.textContent === "Pictures",
    )!;

    await act(async () => fireEvent.mouseEnter(untouchedLabel.closest(".group")!));
    expect(mocks.getSaved).not.toHaveBeenCalled();
  });

  it("keeps live edits and an explicitly cleared form ahead of a late batch", async () => {
    const batch = deferredSearches();

    options(true, [
      { ...saved("first"), name: "" },
      { ...saved("second"), name: "" },
    ]);
    mocks.getAllSaved.mockReturnValue(batch.promise);
    await render();
    await act(async () =>
      mocks.tabProps
        .get("first")
        .onSearchFormChange("first", resolvedSearch("first", "Live filter").search),
    );
    await act(async () =>
      batch.resolve({
        data: [resolvedSearch("first", "Old filter"), resolvedSearch("second", "Untouched filter")],
      }),
    );
    expect(host.textContent).toContain("Live filter");
    expect(host.textContent).toContain("Untouched filter");
    expect(host.textContent).not.toContain("Old filter");

    await act(async () => mocks.tabProps.get("first").onSearchFormChange("first", undefined));
    expect(host.textContent).toContain("common.action.search 1");
    expect(host.textContent).not.toContain("Old filter");
    expect(host.textContent).not.toContain("Live filter");
  });

  it("preserves a custom name edited while the initial criteria batch is in flight", async () => {
    const batch = deferredSearches();

    options(true, [{ ...saved("first"), name: "" }, saved("second")]);
    mocks.getAllSaved.mockReturnValue(batch.promise);
    await render();
    const firstLabel = [...host.querySelectorAll("span")].find(
      (element) => element.textContent === "common.action.search 1",
    )!;

    await act(async () => fireEvent.doubleClick(firstLabel));
    const input = host.querySelector("input")!;

    await act(async () => fireEvent.change(input, { target: { value: "My new name" } }));
    await act(async () => fireEvent.keyDown(input, { key: "Enter" }));
    await act(async () =>
      batch.resolve({
        data: [
          resolvedSearch("first", "Batch filter", "Stale server name"),
          resolvedSearch("second", "Second filter"),
        ],
      }),
    );
    expect(host.textContent).toContain("My new name");
    expect(host.textContent).not.toContain("Batch filter");
    expect(host.textContent).not.toContain("Stale server name");
    expect(mocks.rename).toHaveBeenCalledWith("My new name", { id: "first" });
  });

  it("discards old responses when the local library context changes", async () => {
    const oldBatch = deferredSearches();
    const currentBatch = deferredSearches();

    options(true, [{ ...saved("first"), name: "" }, saved("second")]);
    mocks.getAllSaved
      .mockReturnValueOnce(oldBatch.promise)
      .mockReturnValueOnce(currentBatch.promise);
    await render();
    const oldSignal = mocks.getAllSaved.mock.calls[0][0].signal as AbortSignal;

    mocks.libraryEpoch = "replacement-library";
    await render();
    expect(oldSignal.aborted).toBe(true);
    await act(async () =>
      currentBatch.resolve({ data: [resolvedSearch("first", "Current library")] }),
    );
    await act(async () => oldBatch.resolve({ data: [resolvedSearch("first", "Old library")] }));
    expect(host.textContent).toContain("Current library");
    expect(host.textContent).not.toContain("Old library");
  });

  it("does not resurrect tabs deleted while the initial batch is loading", async () => {
    const batch = deferredSearches();

    options(true, [saved("first"), { ...saved("second"), name: "" }, saved("third")]);
    mocks.getAllSaved.mockReturnValue(batch.promise);
    await render();
    const removedLabel = [...host.querySelectorAll("span")].find(
      (element) => element.textContent === "common.action.search 2",
    )!;
    const close = removedLabel.closest("button")!.querySelectorAll('div[role="button"]')[1];

    await act(async () => fireEvent.click(close));
    await act(async () =>
      batch.resolve({
        data: [
          resolvedSearch("first", "First"),
          resolvedSearch("second", "Deleted filter"),
          resolvedSearch("third", "Third"),
        ],
      }),
    );
    expect(mocks.remove).toHaveBeenCalledWith({ id: "second" });
    expect(host.textContent).not.toContain("Deleted filter");
    expect(host.textContent).not.toContain("common.action.search 2");
    expect(host.textContent).toContain("Saved first");
    expect(host.textContent).toContain("Saved third");
    expect(mocks.getAllSaved).toHaveBeenCalledTimes(1);
  });

  it("retries only a hovered missing tab after a failed batch", async () => {
    options(true, [saved("first"), { ...saved("second"), name: "" }]);
    mocks.getAllSaved.mockRejectedValue(new Error("Unavailable"));
    mocks.getSaved.mockResolvedValue({ data: resolvedSearch("second", "Recovered criteria") });
    await render();
    expect(mocks.getSaved).not.toHaveBeenCalled();
    const missingLabel = [...host.querySelectorAll("span")].find(
      (element) => element.textContent === "common.action.search 2",
    )!;

    await act(async () => fireEvent.mouseEnter(missingLabel.closest(".group")!));
    expect(mocks.getSaved).toHaveBeenCalledTimes(1);
    expect(mocks.getSaved.mock.calls[0][0]).toEqual({ id: "second" });
    expect(host.textContent).toContain("Recovered criteria");
  });
});
