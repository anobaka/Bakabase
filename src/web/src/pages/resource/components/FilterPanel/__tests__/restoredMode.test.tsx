import type { ComponentProps, ReactNode } from "react";
import type { ResourceFilterControllerProps } from "@/components/ResourceFilter/components/ResourceFilterController";
import type { SearchFilterGroup } from "@/components/ResourceFilter/models";
import type { SearchForm } from "@/pages/resource/models";

import { HeroUIProvider } from "@heroui/react";
import { createRoot } from "react-dom/client";
import { act } from "react-dom/test-utils";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import FilterPanel from "..";
import { requiresAdvancedFilterMode } from "../utils";

import { GroupCombinator } from "@/components/ResourceFilter/models";
import { FilterDisplayMode, PropertyPool, SearchOperation } from "@/sdk/constants";

const state = vi.hoisted(() => ({
  controller: undefined as ResourceFilterControllerProps | undefined,
}));
vi.mock("@/components/bakaui", async () => ({
  ...(await vi.importActual("@heroui/react")),
  Popover: (await import("@/components/bakaui/components/Popover")).default,
}));
vi.mock("@/components/ResourceFilter", async () => ({
  ...(await vi.importActual("@/components/ResourceFilter/models")),
  ResourceFilterController: (props: ResourceFilterControllerProps) => {
    state.controller = props;
    return <div data-filter-mode={props.filterDisplayMode} />;
  },
}));
vi.mock("../OrderSelector", () => ({ default: () => null }));
vi.mock("../ShortcutsButton", () => ({ default: () => null }));
vi.mock("../MiscellaneousOptions", () => ({ default: () => null }));
vi.mock("@/components/Playlist", () => ({ PlaylistCollection: () => null }));
vi.mock("@/components/Resource/components/CreatePlaceholderResourcesModal", () => ({
  default: () => null,
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: vi.fn() }),
}));
vi.mock("@/components/utils.tsx", () => ({
  buildLogger: () => () => {},
  useTraceUpdate: () => {},
}));
vi.mock("@/hooks/useReferenceValueResourceCounts", () => ({
  ReferenceValueSearchProvider: ({ children }: { children: ReactNode }) => <>{children}</>,
}));

let root: ReturnType<typeof createRoot>;
let host: HTMLDivElement;
beforeEach(() => {
  (globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
  state.controller = undefined;
  host = document.createElement("div");
  document.body.append(host);
  root = createRoot(host);
});
afterEach(async () => {
  await act(async () => root.unmount());
  host.remove();
});

const plainGroup: SearchFilterGroup = {
  combinator: GroupCombinator.And,
  disabled: false,
  filters: [
    {
      propertyId: 2,
      propertyPool: PropertyPool.Custom,
      operation: SearchOperation.Contains,
      dbValue: "keep",
      disabled: false,
    },
  ],
};
const advancedCases: [string, SearchFilterGroup][] = [
  ["OR", { ...plainGroup, combinator: GroupCombinator.Or }],
  ["disabled group", { ...plainGroup, disabled: true }],
  [
    "disabled condition",
    { ...plainGroup, filters: [{ ...plainGroup.filters![0], disabled: true }] },
  ],
  [
    "nested groups",
    {
      ...plainGroup,
      groups: [{ ...plainGroup, groups: [{ ...plainGroup, combinator: GroupCombinator.Or }] }],
    },
  ],
];

function form(group?: SearchFilterGroup): SearchForm {
  return { page: 3, pageSize: 50, keyword: "book", group };
}
async function render(
  searchForm: SearchForm,
  props: Partial<ComponentProps<typeof FilterPanel>> = {},
) {
  await act(async () =>
    root.render(
      <HeroUIProvider disableAnimation>
        <FilterPanel
          searchForm={searchForm}
          reloadResources={vi.fn()}
          onSelectAllChange={vi.fn()}
          {...props}
        />
      </HeroUIProvider>,
    ),
  );
}

describe("restored resource filter mode", () => {
  it.each(advancedCases)(
    "reveals %s on first render without changing the query",
    async (_, group) => {
      const searchForm = form(structuredClone(group));
      const original = structuredClone(searchForm);
      const onSearch = vi.fn().mockResolvedValue(undefined);
      const onSearchFormLiveChange = vi.fn();
      await render(searchForm, { onSearch, onSearchFormLiveChange });
      expect(state.controller!.filterDisplayMode).toBe(FilterDisplayMode.Advanced);
      expect(state.controller!.group).toBe(searchForm.group);
      expect(searchForm).toEqual(original);
      expect(onSearchFormLiveChange).toHaveBeenLastCalledWith(original);
      expect(onSearch).not.toHaveBeenCalled();
      const search = Array.from(host.querySelectorAll<HTMLButtonElement>("button")).find(
        (button) => button.textContent === "resource.search.button",
      );
      expect(search).toBeDefined();
      await act(async () => search!.click());
      expect(onSearch).toHaveBeenCalledExactlyOnceWith(
        expect.objectContaining({ ...original, page: 1 }),
        false,
      );
    },
  );

  it("promotes a mounted simple panel when a complex saved query arrives", async () => {
    await render(form(structuredClone(plainGroup)));
    expect(state.controller!.filterDisplayMode).toBe(FilterDisplayMode.Simple);
    const restored = form(structuredClone(advancedCases[3][1]));
    const original = structuredClone(restored);
    const onSearch = vi.fn();
    await render(restored, { onSearch });
    expect(state.controller!.filterDisplayMode).toBe(FilterDisplayMode.Advanced);
    expect(state.controller!.group).toBe(restored.group);
    expect(restored).toEqual(original);
    expect(onSearch).not.toHaveBeenCalled();
  });

  it("preserves the user's advanced mode when the restored query needs only simple controls", async () => {
    await render(form());
    expect(state.controller!.filterDisplayMode).toBe(FilterDisplayMode.Simple);
    await act(async () => state.controller!.onFilterDisplayModeChange!(FilterDisplayMode.Advanced));
    await render(form(structuredClone(plainGroup)));
    expect(state.controller!.filterDisplayMode).toBe(FilterDisplayMode.Advanced);
    expect(requiresAdvancedFilterMode(undefined)).toBe(false);
    expect(requiresAdvancedFilterMode(plainGroup)).toBe(false);
  });
});
