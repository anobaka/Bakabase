import type { ReactNode } from "react";
import type { FilterConfig, SearchFilter, SearchFilterGroup } from "../../../models";
import type { IProperty } from "@/components/Property/models";

import { act } from "react-dom/test-utils";
import { createRoot, type Root } from "react-dom/client";
import { HeroUIProvider } from "@heroui/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import Filter from "..";
import FilterGroup from "../../FilterGroup";
import PropertyField from "../PropertyField";
import { FilterProvider } from "../../../context/FilterContext";
import { GroupCombinator } from "../../../models";

import {
  FilterDisplayMode,
  PropertyPool,
  PropertyType,
  ResourceProperty,
  SearchOperation,
  StandardValueType,
} from "@/sdk/constants";

vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("@/i18n", () => ({ getEnumKey: (type: string, value: string) => `${type}.${value}` }));
vi.mock("@/components/utils", () => ({ buildLogger: () => () => {} }));
vi.mock("@/components/bakaui", async () => ({
  ...(await vi.importActual("@heroui/react")),
  Button: (await import("@/components/bakaui/components/Button")).Button,
  Popover: (await import("@/components/bakaui/components/Popover")).default,
}));
vi.mock("../../FilterAddPopoverContent", () => ({
  default: ({
    onAddFilter,
    onAddFilterGroup,
    onClose,
  }: {
    onAddFilter: (auto?: boolean) => void;
    onAddFilterGroup: () => void;
    onClose: () => void;
  }) => (
    <>
      <button
        onClick={() => {
          onClose();
          onAddFilter(true);
        }}
      >
        Add condition
      </button>
      <button
        onClick={() => {
          onClose();
          onAddFilterGroup();
        }}
      >
        Add group
      </button>
    </>
  ),
}));

const property = (
  id = 1,
  type = PropertyType.SingleLineText,
  name = `Property ${id}`,
): IProperty => ({
  id,
  type,
  name,
  pool: PropertyPool.Custom,
  dbValueType: type === PropertyType.Tags ? StandardValueType.ListString : StandardValueType.String,
  bizValueType: type === PropertyType.Tags ? StandardValueType.ListTag : StandardValueType.String,
  typeName: PropertyType[type],
  poolName: "Custom",
  order: 0,
});
const filter = (id = 1, type = PropertyType.SingleLineText): SearchFilter => ({
  disabled: false,
  propertyId: id,
  propertyPool: PropertyPool.Custom,
  property: property(id, type),
  valueProperty: property(id, type),
  operation: SearchOperation.Contains,
  availableOperations: [SearchOperation.Contains, SearchOperation.Equals, SearchOperation.IsNull],
  dbValue: "original",
  bizValue: "Original display value",
});
const group = (filters: SearchFilter[] = [filter()]): SearchFilterGroup => ({
  combinator: GroupCombinator.And,
  disabled: false,
  filters,
});
let config: FilterConfig;
let host: HTMLDivElement;
let root: Root;

async function render(content: ReactNode) {
  await act(async () =>
    root.render(
      <HeroUIProvider disableAnimation>
        <FilterProvider config={config}>{content}</FilterProvider>
      </HeroUIProvider>,
    ),
  );
}
function button(label: string) {
  const found = [...document.querySelectorAll<HTMLButtonElement>("button")].find(
    (element) => (element.getAttribute("aria-label") ?? element.textContent?.trim()) === label,
  );

  if (!found) throw new Error(`Missing button: ${label}`);

  return found;
}
async function click(element: Element) {
  await act(async () => {
    (element as HTMLElement).click();
  });
}
beforeEach(() => {
  vi.stubGlobal("IS_REACT_ACT_ENVIRONMENT", true);
  config = {
    api: {
      getAvailableOperations: vi.fn().mockResolvedValue([SearchOperation.Contains]),
      getAvailableOperationsByPropertyType: vi.fn().mockResolvedValue([SearchOperation.Contains]),
      getValueProperty: vi.fn(async (current) => current.property),
      saveRecentFilter: vi.fn().mockResolvedValue(undefined),
      getRecentFilters: vi.fn().mockResolvedValue([]),
    },
    renderers: {
      openPropertySelector: vi.fn(),
      renderValueInput: vi.fn((current, dbValue, bizValue, onChange, options) =>
        options?.isReadonly ? (
          <span data-value={current.id}>{bizValue ?? dbValue}</span>
        ) : (
          <button
            aria-label={`Edit value ${current.id}`}
            onClick={() => onChange("next-db", "Next display value")}
          >
            {bizValue ?? dbValue ?? "Empty"}
          </button>
        ),
      ),
    },
  };
  host = document.createElement("div");
  document.body.appendChild(host);
  root = createRoot(host);
});
afterEach(async () => {
  await act(async () => root.unmount());
  host.remove();
  vi.unstubAllGlobals();
});

describe("filter condition interactions", () => {
  it("replaces a property through the shared selector and clears the previous property's values", async () => {
    const changed = vi.fn();

    await render(<Filter filter={filter()} onChange={changed} />);
    await click(button("Property 1"));
    expect(config.renderers.openPropertySelector).toHaveBeenCalledWith(
      { id: 1, pool: PropertyPool.Custom },
      expect.any(Function),
      undefined,
    );
    const select = vi.mocked(config.renderers.openPropertySelector).mock.calls[0][1];

    await act(async () => select(property(2, PropertyType.Tags), [SearchOperation.Contains]));
    expect(changed).toHaveBeenLastCalledWith(
      expect.objectContaining({
        propertyId: 2,
        propertyPool: PropertyPool.Custom,
        operation: SearchOperation.Contains,
        dbValue: undefined,
        bizValue: undefined,
      }),
    );
    expect(button("Edit value 2")).toHaveTextContent("Empty");
  });

  it("selects a null operation without retaining or displaying the old standard value", async () => {
    const changed = vi.fn();

    await render(<Filter filter={filter()} onChange={changed} />);
    await click(button("resourceFilter.condition.changeOperation"));
    const nullItem = [...document.querySelectorAll('[role="menuitemradio"]')].find((item) =>
      item.textContent?.includes("SearchOperation.IsNull"),
    )!;

    expect(nullItem).toBeDefined();
    await click(nullItem);
    expect(changed).toHaveBeenLastCalledWith(
      expect.objectContaining({
        operation: SearchOperation.IsNull,
        dbValue: undefined,
        bizValue: undefined,
      }),
    );
    expect(document.body).not.toHaveTextContent("Original display value");
  });

  it("keeps a disabled condition readable and allows re-enabling or removing it", async () => {
    const changed = vi.fn();
    const remove = vi.fn();

    await render(
      <Filter filter={{ ...filter(), disabled: true }} onChange={changed} onRemove={remove} />,
    );
    expect(document.body).toHaveTextContent("Original display value");
    expect(document.body).toHaveTextContent("resourceFilter.condition.disabled");
    await click(button("resourceFilter.condition.enable"));
    expect(changed).toHaveBeenLastCalledWith(
      expect.objectContaining({ disabled: false, dbValue: "original" }),
    );
    await click(button("resourceFilter.condition.remove"));
    expect(remove).toHaveBeenCalledOnce();
  });

  it.each([PropertyType.SingleLineText, PropertyType.Number, PropertyType.Tags])(
    "preserves standard values and editing options while layout and mode change for type %s",
    async (type) => {
      const current = {
        ...filter(1, type),
        dbValue: type === PropertyType.Number ? "0" : "tag-a,tag-b",
        bizValue: "Long displayed value",
      };
      const changed = vi.fn();

      await render(
        <Filter
          filter={current}
          filterDisplayMode={FilterDisplayMode.Simple}
          layout="vertical"
          onChange={changed}
        />,
      );
      expect(config.renderers.renderValueInput).toHaveBeenLastCalledWith(
        current.valueProperty,
        current.dbValue,
        current.bizValue,
        expect.any(Function),
        expect.objectContaining({ isEditing: true, operation: SearchOperation.Contains }),
      );
      await render(
        <Filter
          filter={current}
          filterDisplayMode={FilterDisplayMode.Advanced}
          layout="horizontal"
          onChange={changed}
        />,
      );
      expect(config.renderers.renderValueInput).toHaveBeenLastCalledWith(
        current.valueProperty,
        current.dbValue,
        current.bizValue,
        expect.any(Function),
        expect.objectContaining({ isEditing: undefined }),
      );
      expect(changed).not.toHaveBeenCalled();
      await click(button("Edit value 1"));
      expect(changed).toHaveBeenLastCalledWith(
        expect.objectContaining({ dbValue: "next-db", bizValue: "Next display value" }),
      );
    },
  );

  it("does not open selectors or expose mutation actions in readonly mode", async () => {
    const changed = vi.fn();

    await render(
      <Filter
        autoTriggerPropertySelector
        isReadonly
        filter={{ disabled: false }}
        onChange={changed}
      />,
    );
    expect(config.renderers.openPropertySelector).not.toHaveBeenCalled();
    expect(document.querySelector("button")).toBeNull();
    await render(
      <Filter
        isReadonly
        filter={filter()}
        filterDisplayMode={FilterDisplayMode.Simple}
        onChange={changed}
      />,
    );
    expect(document.body).toHaveTextContent("Original display value");
    expect(document.querySelector("button")).toBeNull();
    expect(config.renderers.renderValueInput).toHaveBeenLastCalledWith(
      expect.anything(),
      "original",
      "Original display value",
      expect.any(Function),
      expect.objectContaining({ isReadonly: true, isEditing: undefined }),
    );
    expect(changed).not.toHaveBeenCalled();
  });

  it("offers source help only for the built-in source property", async () => {
    await render(
      <PropertyField
        isReadonly
        property={{ ...property(ResourceProperty.Source), pool: PropertyPool.Internal }}
      />,
    );
    expect(button("resourceFilter.source.about")).toBeInTheDocument();
    await render(<PropertyField isReadonly property={property(ResourceProperty.Source)} />);
    expect(document.querySelector("button")).toBeNull();
  });
});

describe("filter groups", () => {
  it("adds and removes a nested group and discards a cancelled new condition", async () => {
    const changed = vi.fn();

    await render(<FilterGroup isRoot group={group([])} onChange={changed} />);
    await click(button("resourceFilter.group.addCondition"));
    await click(button("Add group"));
    expect(changed).toHaveBeenLastCalledWith(
      expect.objectContaining({
        groups: [expect.objectContaining({ combinator: GroupCombinator.And, disabled: false })],
      }),
    );
    await click(button("resourceFilter.group.actions"));
    await click(button("resourceFilter.group.remove"));
    expect(changed).toHaveBeenLastCalledWith(expect.objectContaining({ groups: [] }));
    await click(button("resourceFilter.group.addCondition"));
    await click(button("Add condition"));
    expect(config.renderers.openPropertySelector).toHaveBeenCalledOnce();
    const cancel = vi.mocked(config.renderers.openPropertySelector).mock.calls[0][2];

    expect(cancel).toBeTypeOf("function");
    await act(async () => {
      cancel?.();
    });
    expect(changed).toHaveBeenLastCalledWith(expect.objectContaining({ filters: [] }));
  });

  it("changes only the selected nested group's AND/OR combinator", async () => {
    const changed = vi.fn();
    const nested = { ...group([filter(2)]), combinator: GroupCombinator.Or };

    await render(
      <FilterGroup
        isRoot
        filterLayout="vertical"
        group={{ ...group(), groups: [nested] }}
        onChange={changed}
      />,
    );
    const logicButtons = [
      ...document.querySelectorAll('[aria-label="resourceFilter.group.changeLogic"]'),
    ];

    expect(logicButtons).toHaveLength(2);
    await click(logicButtons[1]);
    const next = changed.mock.calls[0][0];

    expect(next.combinator).toBe(GroupCombinator.And);
    expect(next.groups[0].combinator).toBe(GroupCombinator.And);
    expect(next.groups[0].filters[0].dbValue).toBe("original");
    expect(next.filters[0].propertyId).toBe(1);
  });

  it("keeps a disabled group's menu accessible so the group can be restored", async () => {
    const changed = vi.fn();

    await render(
      <FilterGroup
        filterLayout="vertical"
        group={{ ...group(), disabled: true }}
        onChange={changed}
      />,
    );
    await click(button("resourceFilter.group.actions"));
    await click(button("resourceFilter.group.enable"));
    expect(changed).toHaveBeenLastCalledWith(expect.objectContaining({ disabled: false }));
    expect(document.body).toHaveTextContent("Original display value");
  });

  it("keeps later conditions attached to their own values when the first condition is removed", async () => {
    const changed = vi.fn();

    await render(
      <FilterGroup
        isRoot
        filterLayout="vertical"
        group={group([filter(1), filter(2)])}
        onChange={changed}
      />,
    );
    await click(button("resourceFilter.condition.remove"));
    expect(document.querySelector('[aria-label="Edit value 1"]')).toBeNull();
    await click(button("Edit value 2"));
    expect(changed).toHaveBeenLastCalledWith(
      expect.objectContaining({
        filters: [expect.objectContaining({ propertyId: 2, dbValue: "next-db" })],
      }),
    );
  });

  it("renders readonly grouped conditions without interactive logical controls", async () => {
    const changed = vi.fn();

    await render(
      <FilterGroup
        isReadonly
        group={{ ...group(), groups: [group([filter(2)])] }}
        onChange={changed}
      />,
    );
    expect(document.body).toHaveTextContent("resourceFilter.group.matchAll");
    expect(document.querySelector("button, [role=button], [tabindex='0']")).toBeNull();
    expect(changed).not.toHaveBeenCalled();
  });
});
