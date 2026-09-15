import type { IProperty } from "@/components/Property/models";
import type { SearchFilterGroup } from "../../../models";

import { useState } from "react";
import { act } from "react-dom/test-utils";
import { createRoot, type Root } from "react-dom/client";
import { HeroUIProvider } from "@heroui/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import ResourceFilterController from "../../ResourceFilterController";
import { GroupCombinator } from "../../../models";

import {
  createResourceFilterFixtureConfig,
  getFixtureOperations,
} from "@/pages/test/cases/resourceFilterFixtures";
import {
  FilterDisplayMode,
  PropertyPool,
  PropertyType,
  SearchOperation,
  StandardValueType,
} from "@/sdk/constants";

vi.mock("@/components/bakaui", async () => ({
  ...(await vi.importActual("@heroui/react")),
  Button: (await import("@/components/bakaui/components/Button")).Button,
  Popover: (await import("@/components/bakaui/components/Popover")).default,
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: vi.fn() }),
}));
vi.mock("../../../presets/DefaultFilterPreset", () => ({
  createDefaultFilterConfig: () => undefined,
}));
vi.mock("@/components/ResourceKeywordAutocomplete", () => ({ default: () => null }));
vi.mock("@/components/Property/components/PropertyValueRenderer", () => ({
  default: ({ bizValue }: { bizValue?: string }) => <span>{bizValue}</span>,
}));
vi.mock("@/i18n", () => ({ getEnumKey: (type: string, name: string) => `${type}.${name}` }));
vi.mock("@/components/utils", () => ({ buildLogger: () => () => {} }));
vi.mock("@/sdk/BApi", () => ({ default: {} }));

const title: IProperty = {
  id: 100,
  pool: PropertyPool.Custom,
  name: "Title",
  type: PropertyType.SingleLineText,
  dbValueType: StandardValueType.String,
  bizValueType: StandardValueType.String,
  typeName: "SingleLineText",
  poolName: "Custom",
  order: 0,
};
const config = createResourceFilterFixtureConfig([title], vi.fn());
const variants = [
  { filterDisplayMode: FilterDisplayMode.Simple, filterLayout: "vertical" as const },
  { filterDisplayMode: FilterDisplayMode.Simple, filterLayout: "horizontal" as const },
  { filterDisplayMode: FilterDisplayMode.Advanced, filterLayout: "vertical" as const },
  { filterDisplayMode: FilterDisplayMode.Advanced, filterLayout: "horizontal" as const },
];

function SharedControllers() {
  const [group, setGroup] = useState<SearchFilterGroup>({
    combinator: GroupCombinator.And,
    disabled: false,
    filters: [
      {
        disabled: false,
        propertyPool: title.pool,
        propertyId: title.id,
        property: title,
        valueProperty: title,
        operation: SearchOperation.Contains,
        availableOperations: getFixtureOperations(title.type),
        dbValue: '"example"',
        bizValue: "example",
      },
    ],
  });

  return (
    <>
      <output>{JSON.stringify(group.filters?.[0])}</output>
      {variants.map((variant, index) => (
        <section key={index} data-controller={index}>
          <ResourceFilterController
            {...variant}
            config={config}
            group={group}
            onGroupChange={setGroup}
            showRecentFilters={false}
            showTags={false}
          />
        </section>
      ))}
    </>
  );
}

let root: Root;
let host: HTMLDivElement;
async function mouseClick(element: HTMLElement) {
  await act(async () => {
    const init = { bubbles: true, cancelable: true, button: 0, pointerType: "mouse", pointerId: 1 };
    element.dispatchEvent(new PointerEvent("pointerover", init));
    element.dispatchEvent(new MouseEvent("mouseover", init));
    element.dispatchEvent(new PointerEvent("pointerdown", { ...init, buttons: 1 }));
    element.dispatchEvent(new MouseEvent("mousedown", { ...init, buttons: 1 }));
    element.focus();
    element.dispatchEvent(new PointerEvent("pointerup", init));
    element.dispatchEvent(new MouseEvent("mouseup", init));
    element.dispatchEvent(new MouseEvent("click", { ...init, detail: 1 }));
  });
}
beforeEach(async () => {
  vi.stubGlobal("IS_REACT_ACT_ENVIRONMENT", true);
  host = document.createElement("div");
  document.body.append(host);
  root = createRoot(host);
  await act(async () =>
    root.render(
      <HeroUIProvider disableAnimation>
        <SharedControllers />
      </HeroUIProvider>,
    ),
  );
});
afterEach(async () => {
  await act(async () => root.unmount());
  host.remove();
  vi.unstubAllGlobals();
});

describe("operation menu pointer and keyboard interaction", () => {
  it.each([0, 1, 2, 3])(
    "propagates a mouse selection from controller %i to all shared groups",
    async (index) => {
      const trigger = host.querySelector<HTMLButtonElement>(
        `[data-controller="${index}"] button[aria-label="resourceFilter.condition.changeOperation"]`,
      )!;
      await mouseClick(trigger);
      const item = [...document.querySelectorAll<HTMLElement>('[role="menuitemradio"]')].find(
        (element) => element.textContent?.includes("SearchOperation.StartsWith"),
      )!;
      expect(item).toBeDefined();
      await mouseClick(item);
      const actual = JSON.parse(host.querySelector("output")!.textContent!);
      expect(actual).toMatchObject({
        operation: SearchOperation.StartsWith,
        dbValue: '"example"',
        bizValue: "example",
      });
      const labels = [
        ...host.querySelectorAll('button[aria-label="resourceFilter.condition.changeOperation"]'),
      ].map((element) => element.textContent);
      expect(labels).toHaveLength(4);
      labels.forEach((label) => expect(label).toContain("SearchOperation.StartsWith"));
    },
  );

  it("retains the same controlled selection when an operation is selected with Enter", async () => {
    const trigger = host.querySelector<HTMLButtonElement>(
      'button[aria-label="resourceFilter.condition.changeOperation"]',
    )!;
    await mouseClick(trigger);
    const items = [...document.querySelectorAll<HTMLElement>('[role="menuitemradio"]')];
    expect(
      items.find((item) => item.textContent?.includes("SearchOperation.Contains")),
    ).toHaveAttribute("aria-checked", "true");
    const item = items.find((element) =>
      element.textContent?.includes("SearchOperation.StartsWith"),
    )!;
    await act(async () => {
      item.focus();
      item.dispatchEvent(
        new KeyboardEvent("keydown", {
          key: "Enter",
          code: "Enter",
          bubbles: true,
          cancelable: true,
        }),
      );
      item.dispatchEvent(
        new KeyboardEvent("keyup", {
          key: "Enter",
          code: "Enter",
          bubbles: true,
          cancelable: true,
        }),
      );
    });
    expect(JSON.parse(host.querySelector("output")!.textContent!)).toMatchObject({
      operation: SearchOperation.StartsWith,
      dbValue: '"example"',
      bizValue: "example",
    });
    await mouseClick(trigger);
    const selectedItem = [...document.querySelectorAll<HTMLElement>('[role="menuitemradio"]')].find(
      (element) => element.textContent?.includes("SearchOperation.StartsWith"),
    )!;
    expect(selectedItem).toHaveAttribute("aria-checked", "true");
  });
});
