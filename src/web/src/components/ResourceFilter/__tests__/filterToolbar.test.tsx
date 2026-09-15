import type { ReactNode } from "react";
import type { InputProps } from "@heroui/react";
import type { FilterConfig, SearchFilter, SearchFilterGroup } from "../models";
import type { FilterPortalProps } from "../components/FilterPortal";

import { HeroUIProvider, Input } from "@heroui/react";
import { act } from "react-dom/test-utils";
import { createRoot } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import FilterPortal from "../components/FilterPortal";
import RecentFilters from "../components/RecentFilters";
import ResourceFilterController from "../components/ResourceFilterController";
import ResourceSearchPanel from "../components/ResourceSearchPanel";
import { FilterProvider } from "../context/FilterContext";
import { GroupCombinator } from "../models";

import {
  FilterDisplayMode,
  PropertyPool,
  PropertyType,
  ResourceTag,
  SearchOperation,
  StandardValueType,
} from "@/sdk/constants";

const state = vi.hoisted(() => ({
  config: undefined as FilterConfig | undefined,
  createPortal: vi.fn(),
}));

vi.mock("@/components/bakaui", async () => ({
  ...(await vi.importActual("@heroui/react")),
  Button: (await import("@/components/bakaui/components/Button")).Button,
  Popover: (await import("@/components/bakaui/components/Popover")).default,
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: state.createPortal }),
}));
vi.mock("../presets/DefaultFilterPreset", () => ({
  createDefaultFilterConfig: () => state.config,
}));
vi.mock("@/components/ResourceKeywordAutocomplete", () => ({
  default: (props: InputProps) => <Input {...props} />,
}));
vi.mock("../components/Filter", () => ({
  default: ({ filter, isReadonly }: { filter: SearchFilter; isReadonly?: boolean }) => (
    <div data-filter-readonly={isReadonly}>{filter.dbValue}</div>
  ),
}));
vi.mock("../components/FilterGroup", () => ({
  default: ({ group }: { group: SearchFilterGroup }) => (
    <div data-filter-group>{JSON.stringify(group)}</div>
  ),
}));
vi.mock("../components/FilterGroupWithContext", () => ({
  default: ({ group, isReadonly }: { group?: SearchFilterGroup; isReadonly?: boolean }) => (
    <div data-filter-group data-readonly={isReadonly}>
      {JSON.stringify(group)}
    </div>
  ),
}));
vi.mock("@/i18n", () => ({ getEnumKey: (type: string, name: string) => `${type}.${name}` }));

const numberProperty = {
  id: 7,
  name: "Pages",
  pool: PropertyPool.Custom,
  type: PropertyType.Number,
  typeName: "Number",
  poolName: "Custom",
  dbValueType: StandardValueType.Decimal,
  bizValueType: StandardValueType.Decimal,
  order: 0,
};
let root: ReturnType<typeof createRoot>;
let container: HTMLDivElement;
let extraContainers: HTMLDivElement[];

beforeEach(() => {
  (globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
  container = document.createElement("div");
  document.body.append(container);
  root = createRoot(container);
  extraContainers = [];
  state.config = {
    api: {
      getAvailableOperations: vi.fn().mockResolvedValue([]),
      getAvailableOperationsByPropertyType: vi.fn().mockResolvedValue([]),
      getValueProperty: vi.fn().mockResolvedValue(numberProperty),
      getRecentFilters: vi.fn().mockResolvedValue([]),
      saveRecentFilter: vi.fn().mockResolvedValue(undefined),
    },
    renderers: { openPropertySelector: vi.fn(), renderValueInput: vi.fn() },
  };
});
afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  extraContainers.forEach((element) => element.remove());
});

async function render(children: ReactNode) {
  await act(async () =>
    root.render(
      <HeroUIProvider disableAnimation>
        <FilterProvider config={state.config!}>{children}</FilterProvider>
      </HeroUIProvider>,
    ),
  );
}
function button(text: string, surface: ParentNode = document.body) {
  const result = Array.from(surface.querySelectorAll<HTMLButtonElement>("button")).find(
    (element) => element.textContent === text || element.getAttribute("aria-label") === text,
  );
  expect(result, `Button ${text}`).toBeDefined();
  return result!;
}
async function press(element: HTMLElement) {
  await act(async () => element.click());
}
function portalProps(overrides: Partial<FilterPortalProps> = {}): FilterPortalProps {
  return {
    mode: FilterDisplayMode.Simple,
    showRecentFilters: false,
    showTags: false,
    onAddFilter: vi.fn(),
    onAddFilterGroup: vi.fn(),
    onSelectFilters: vi.fn(),
    onModeChange: vi.fn(),
    ...overrides,
  };
}
function portalContainer() {
  const result = document.createElement("div");
  document.body.append(result);
  extraContainers.push(result);
  return result;
}

describe("resource filter toolbar", () => {
  it("shows both modes and preserves the two-filter range selection in Simple mode", async () => {
    const props = portalProps();
    await render(<FilterPortal {...props} />);
    expect(button("resourceFilter.toolbar.simple")).toHaveAttribute("aria-pressed", "true");
    expect(button("resourceFilter.toolbar.advanced")).toHaveAttribute("aria-pressed", "false");
    await press(button("resourceFilter.toolbar.addCondition"));
    expect(state.config!.renderers.openPropertySelector).toHaveBeenCalledTimes(1);
    const onSelect = vi.mocked(state.config!.renderers.openPropertySelector).mock.calls[0][1];
    await act(async () =>
      onSelect(numberProperty, [
        SearchOperation.GreaterThanOrEquals,
        SearchOperation.LessThanOrEquals,
      ]),
    );
    expect(state.config!.api.getValueProperty).toHaveBeenCalledTimes(2);
    expect(props.onSelectFilters).toHaveBeenCalledWith([
      expect.objectContaining({
        propertyId: 7,
        operation: SearchOperation.GreaterThanOrEquals,
        valueProperty: numberProperty,
        disabled: false,
      }),
      expect.objectContaining({
        propertyId: 7,
        operation: SearchOperation.LessThanOrEquals,
        valueProperty: numberProperty,
        disabled: false,
      }),
    ]);
    expect(props.onAddFilter).not.toHaveBeenCalled();
    await press(button("resourceFilter.toolbar.advanced"));
    expect(props.onModeChange).toHaveBeenCalledWith(FilterDisplayMode.Advanced);
  });

  it.each([false, true])(
    "closes the Advanced popover after adding a condition or group (group=%s)",
    async (isGroup) => {
      const props = portalProps({ mode: FilterDisplayMode.Advanced });
      await render(<FilterPortal {...props} />);
      await press(button("resourceFilter.toolbar.addCondition"));
      const key = isGroup ? "resourceFilter.filterGroup" : "resourceFilter.filter";
      const target = Array.from(document.querySelectorAll<HTMLButtonElement>("button")).find(
        (element) => element.querySelector(".font-medium")?.textContent === key,
      )!;
      expect(target).toBeDefined();
      await press(target);
      if (isGroup) {
        expect(props.onAddFilterGroup).toHaveBeenCalledExactlyOnceWith();
        expect(props.onAddFilter).not.toHaveBeenCalled();
      } else {
        expect(props.onAddFilter).toHaveBeenCalledExactlyOnceWith(true);
        expect(props.onAddFilterGroup).not.toHaveBeenCalled();
      }
      expect(document.body.textContent).not.toContain("resourceFilter.add.description");
    },
  );

  it("keeps keyword actions and filter groups in their external containers and removes actions in readonly mode", async () => {
    const keyword = portalContainer();
    const actions = portalContainer();
    const groups = portalContainer();
    const onSearch = vi.fn();
    const onGroupChange = vi.fn();
    const props = {
      keyword: "book",
      onSearch,
      onGroupChange,
      keywordContainer: keyword,
      filterPortalContainer: actions,
      filterGroupsContainer: groups,
    };
    await render(<ResourceFilterController {...props} />);
    expect(keyword.querySelector("input")).toHaveValue("book");
    expect(button("resourceFilter.toolbar.addCondition", actions)).toBeDefined();
    expect(groups.querySelector("[data-filter-group]")).toBeDefined();
    await act(async () => {
      keyword
        .querySelector("input")!
        .dispatchEvent(new KeyboardEvent("keydown", { key: "Enter", bubbles: true }));
    });
    expect(onSearch).toHaveBeenCalledTimes(1);
    await render(<ResourceFilterController {...props} isReadonly />);
    expect(keyword).toBeEmptyDOMElement();
    expect(actions).toBeEmptyDOMElement();
    expect(groups.querySelector("[data-filter-group]")).toHaveAttribute("data-readonly", "true");
    expect(onGroupChange).not.toHaveBeenCalled();
  });

  it("preserves the existing Advanced-to-Simple cleanup and informs the controlled parent", async () => {
    const active = { propertyId: 1, dbValue: "keep", disabled: false };
    const group: SearchFilterGroup = {
      combinator: GroupCombinator.Or,
      disabled: true,
      filters: [active, { disabled: true, dbValue: "remove" }],
      groups: [{ combinator: GroupCombinator.And, disabled: false, filters: [] }],
    };
    const onGroupChange = vi.fn();
    const onModeChange = vi.fn();
    await render(
      <ResourceFilterController
        filterDisplayMode={FilterDisplayMode.Advanced}
        group={group}
        onGroupChange={onGroupChange}
        onFilterDisplayModeChange={onModeChange}
      />,
    );
    await press(button("resourceFilter.toolbar.simple"));
    expect(onGroupChange).toHaveBeenCalledExactlyOnceWith({
      ...group,
      combinator: GroupCombinator.And,
      disabled: false,
      filters: [active],
      groups: [],
    });
    expect(onModeChange).toHaveBeenCalledExactlyOnceWith(FilterDisplayMode.Simple);
  });

  it("retries a recent-filter failure and adds the original readonly condition", async () => {
    const filter: SearchFilter = {
      propertyId: 5,
      disabled: false,
      dbValue: "recent value",
      operation: SearchOperation.Contains,
    };
    vi.mocked(state.config!.api.getRecentFilters)
      .mockRejectedValueOnce(new Error("offline"))
      .mockResolvedValueOnce([filter]);
    const onSelect = vi.fn();
    await render(<RecentFilters onSelectFilter={onSelect} />);
    expect(container.querySelector('[role="alert"]')).toHaveTextContent(
      "resourceFilter.recent.failed",
    );
    await press(button("resourceFilter.recent.retry"));
    expect(container.querySelector("[data-filter-readonly]")).toHaveAttribute(
      "data-filter-readonly",
      "true",
    );
    await press(button("resourceFilter.recent.add"));
    expect(onSelect).toHaveBeenCalledExactlyOnceWith(filter);
    expect(onSelect.mock.calls[0][0]).toBe(filter);
    expect(state.config!.api.getRecentFilters).toHaveBeenCalledTimes(2);
  });

  it("shows an empty recent state and ignores a replaced provider's late response", async () => {
    let resolveOld!: (filters: SearchFilter[]) => void;
    vi.mocked(state.config!.api.getRecentFilters).mockReturnValue(
      new Promise((resolve) => {
        resolveOld = resolve;
      }),
    );
    await render(<RecentFilters />);
    expect(container.querySelector('[role="status"]')).toHaveTextContent(
      "resourceFilter.recent.loading",
    );
    state.config = {
      ...state.config!,
      api: { ...state.config!.api, getRecentFilters: vi.fn().mockResolvedValue([]) },
    };
    await render(<RecentFilters />);
    expect(container).toHaveTextContent("resourceFilter.recent.empty");
    await act(async () => resolveOld([{ disabled: false, dbValue: "stale filter" }]));
    expect(container).not.toHaveTextContent("stale filter");
  });

  it("removes a special-tag chip without changing the keyword or group", async () => {
    const group: SearchFilterGroup = {
      combinator: GroupCombinator.Or,
      disabled: false,
      filters: [{ disabled: false, dbValue: "keep" }],
    };
    const criteria = { keyword: "book", group, tags: [ResourceTag.IsParent, ResourceTag.Pinned] };
    const onChange = vi.fn();
    await render(
      <ResourceSearchPanel
        criteria={criteria}
        onChange={onChange}
        showKeyword
        showRecentFilters={false}
        showTags={false}
      />,
    );
    expect(container.querySelector("input")).toHaveValue("book");
    const firstClose = container.querySelector<HTMLElement>('[aria-label="close chip"]')!;
    expect(firstClose).not.toBeNull();
    await press(firstClose);
    expect(onChange).toHaveBeenCalledExactlyOnceWith({ ...criteria, tags: [ResourceTag.Pinned] });
    expect(onChange.mock.calls[0][0].group).toBe(group);
  });
});
