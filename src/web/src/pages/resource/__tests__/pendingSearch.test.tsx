import type * as Zustand from "zustand";
import type {
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsDomainResourceOptionsSavedSearch as SavedSearch,
  BakabaseServiceModelsInputResourceSearchInputModel as SearchInput,
} from "@/sdk/Api";

import { StrictMode } from "react";
import { act } from "react-dom/test-utils";
import { createRoot, type Root } from "react-dom/client";
import { HeroUIProvider } from "@heroui/react";
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
  SearchCombinator,
  SearchOperation,
  StandardValueType,
} from "@/sdk/constants";

const mocks = vi.hoisted(() => ({ save: vi.fn(), getSaved: vi.fn() }));

vi.mock("@/sdk/BApi", () => ({
  default: { resource: { saveNewResourceSearch: mocks.save, getSavedSearch: mocks.getSaved } },
}));
vi.mock("@/components/bakaui", async () => await vi.importActual("@heroui/react"));
vi.mock("@/stores/options", async () => {
  const { create } = await vi.importActual<typeof Zustand>("zustand");

  return {
    useResourceOptionsStore: create(() => ({ initialized: false, data: { savedSearches: [] } })),
  };
});
vi.mock("../components/ResourceTabContent", () => ({
  default: ({ activated, searchId }: { activated: boolean; searchId: string }) => (
    <div data-active={String(activated)} data-search-id={searchId} />
  ),
}));
vi.mock("../components/RecentlyPlayedDrawer", () => ({ default: () => null }));
vi.mock("../components/SearchSummary", () => ({ default: () => null }));
vi.mock("../utils/buildAutoTabName", () => ({ buildAutoTabName: () => "" }));

let host: HTMLDivElement;
let root: Root;

function saved(id: string): SavedSearch {
  return { id, name: `Saved ${id}`, search: { page: 1, pageSize: 50 }, displayMode: 1 };
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
