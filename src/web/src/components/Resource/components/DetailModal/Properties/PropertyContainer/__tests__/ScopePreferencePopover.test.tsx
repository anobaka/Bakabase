import type { ReactElement, ReactNode } from "react";
import type { IProperty } from "@/components/Property/models";
import type { Property } from "@/core/models/Resource";
import type * as Utils from "@/components/utils";

import { act, cloneElement } from "react";
import { createRoot } from "react-dom/client";
import dayjs from "dayjs";
import duration from "dayjs/plugin/duration";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import ScopePreferencePopover from "../ScopePreferencePopover";

import { getBizValueType, getDbValueType } from "@/components/Property/PropertySystem";
import { PropertyPool, PropertyType, PropertyValueScope } from "@/sdk/constants";

const { createPortal, savePreference, onChanged } = vi.hoisted(() => ({
  createPortal: vi.fn(),
  savePreference: vi.fn(),
  onChanged: vi.fn(),
}));

vi.mock("@/sdk/BApi", () => ({
  default: { resource: { putResourcePropertyValueScopePreference: savePreference } },
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal }),
}));
vi.mock("@/components/utils", async (importOriginal) => ({
  ...(await importOriginal<typeof Utils>()),
  buildLogger: () => () => {},
}));
vi.mock("@/components/ResourceFilter/components/ParentResourceValueRenderer", () => ({
  default: () => null,
}));
// Keep the production value renderers and serialization, without loading unrelated
// attachment/editor dependencies from the StandardValue barrel.
vi.mock("@/components/StandardValue", async () => ({
  ...(await import("@/components/StandardValue/helpers")),
  TagsValueRenderer: (
    await import("@/components/StandardValue/ValueRenderer/Renderers/TagsValueRenderer")
  ).default,
  LinkValueRenderer: (
    await import("@/components/StandardValue/ValueRenderer/Renderers/LinkValueRenderer")
  ).default,
  DateTimeValueRenderer: (
    await import("@/components/StandardValue/ValueRenderer/Renderers/DateTimeValueRenderer")
  ).default,
  TimeValueRenderer: (
    await import("@/components/StandardValue/ValueRenderer/Renderers/TimeValueRenderer")
  ).default,
}));
vi.mock("@/components/bakaui", async () => ({
  ...(await import("@heroui/react")),
  // Only replace positioning/portal behavior; chips, buttons and switches are real HeroUI.
  Popover: ({
    trigger,
    children,
    visible,
    onVisibleChange,
  }: {
    trigger: ReactElement;
    children: ReactNode;
    visible: boolean;
    onVisibleChange: (visible: boolean) => void;
  }) => (
    <>
      {cloneElement(trigger, { onClick: () => onVisibleChange(!visible) })}
      {visible && <div role="dialog">{children}</div>}
    </>
  ),
}));

dayjs.extend(duration);

let root: ReturnType<typeof createRoot>;
let container: HTMLDivElement;

beforeEach(() => {
  vi.stubGlobal("IS_REACT_ACT_ENVIRONMENT", true);
  vi.clearAllMocks();
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
});

function propertyOf(type: PropertyType, options?: unknown): IProperty {
  return {
    id: 10,
    pool: PropertyPool.Custom,
    name: "Preview property",
    type,
    dbValueType: getDbValueType(type),
    bizValueType: getBizValueType(type),
    typeName: PropertyType[type],
    poolName: "Custom",
    options,
  };
}

async function openPopover(property: IProperty, values: Property["values"]) {
  await act(async () => {
    root.render(
      <ScopePreferencePopover
        effectivePriority={[PropertyValueScope.Manual, PropertyValueScope.Synchronization]}
        property={property}
        propertyId={property.id}
        propertyPool={property.pool}
        resourceId={5}
        values={values}
        onChanged={onChanged}
      />,
    );
  });
  await act(async () => {
    container.querySelector<HTMLButtonElement>("button")!.click();
  });

  return container.querySelector<HTMLElement>('[role="dialog"]')!;
}

describe("scope preference value previews", () => {
  it("shows all tags as standard colored chips, preserving aliases and escaped punctuation", async () => {
    const tags = [
      { group: "Genre, style", name: "Adventure; story" },
      { group: "Language", name: "Chinese" },
      { name: "Completed" },
      { name: "Favorite" },
      { name: "Offline" },
      { name: "Controller" },
    ];
    const property = propertyOf(PropertyType.Tags, {
      tags: tags.map((tag, index) => ({ ...tag, value: `tag-id-${index}`, color: "#cc2244" })),
    });
    const dialog = await openPopover(property, [
      {
        scope: PropertyValueScope.Manual,
        value: tags.map((_, index) => `tag-id-${index}`),
        bizValue: [{ name: "Before alias" }],
        aliasAppliedBizValue: tags,
      },
    ]);
    const chips = Array.from(dialog.querySelectorAll<HTMLElement>("span")).filter(
      (node) =>
        node.children.length === 0 &&
        tags.some(
          (tag) => node.textContent === (tag.group ? `${tag.group}:${tag.name}` : tag.name),
        ),
    );

    expect(chips).toHaveLength(6);
    expect(chips[0].textContent).toBe("Genre, style:Adventure; story");
    expect(chips[0].parentElement).toHaveStyle({ color: "rgb(204, 34, 68)" });
    expect(dialog.textContent).not.toContain("Before alias");
    expect(dialog.textContent).not.toContain("tag-id-");
    expect(dialog.textContent).not.toContain('{"group"');

    await act(async () => chips[0].click());
    expect(createPortal).not.toHaveBeenCalled();
    expect(onChanged).not.toHaveBeenCalled();
    expect(savePreference).not.toHaveBeenCalled();

    await act(async () => {
      dialog
        .querySelector<HTMLButtonElement>('[title="property.scopePreference.addToList"]')!
        .click();
    });
    await act(async () => {
      Array.from(dialog.querySelectorAll("button"))
        .find((button) => button.textContent === "property.scopePreference.save")!
        .click();
    });
    expect(savePreference).toHaveBeenCalledExactlyOnceWith(5, {
      propertyPool: PropertyPool.Custom,
      propertyId: 10,
      priorities: [{ scope: PropertyValueScope.Manual, fallbackOnEmpty: true }],
    });
  });

  it("keeps each source's own tag values separate", async () => {
    const dialog = await openPopover(propertyOf(PropertyType.Tags), [
      { scope: PropertyValueScope.Manual, bizValue: [{ name: "Manual tag" }] },
      { scope: PropertyValueScope.Synchronization, bizValue: [{ name: "Synced tag" }] },
    ]);
    const labels = Array.from(dialog.querySelectorAll(".font-medium"));

    expect(
      labels.find((label) => label.textContent === "PropertyValueScope.Manual")?.parentElement,
    ).toHaveTextContent("Manual tag");
    expect(
      labels.find((label) => label.textContent === "PropertyValueScope.Manual")?.parentElement,
    ).not.toHaveTextContent("Synced tag");
    expect(
      labels.find((label) => label.textContent === "PropertyValueScope.Synchronization")
        ?.parentElement,
    ).toHaveTextContent("Synced tag");
  });

  it("does not recover raw reference IDs when the resolved business value is empty", async () => {
    const dialog = await openPopover(
      propertyOf(PropertyType.Tags, {
        tags: [{ value: "stale-tag-id", name: "Old option" }],
      }),
      [{ scope: PropertyValueScope.Manual, value: ["stale-tag-id"] }],
    );

    expect(dialog).toHaveTextContent("property.scopePreference.noValues");
    await act(async () =>
      dialog.querySelector<HTMLInputElement>('input[type="checkbox"]')!.click(),
    );

    expect(dialog).toHaveTextContent("common.label.notSet");
    expect(dialog).not.toHaveTextContent("stale-tag-id");
    expect(dialog).not.toHaveTextContent("Old option");
    expect(dialog.querySelector('[title="property.scopePreference.addToList"]')).not.toBeNull();
  });

  it("uses the standard link label for object values instead of JSON", async () => {
    const dialog = await openPopover(propertyOf(PropertyType.Link), [
      {
        scope: PropertyValueScope.Manual,
        bizValue: { text: "Resource, source", url: "https://example.com/share/resource" },
      },
    ]);

    expect(dialog.querySelector("a")).toHaveTextContent("Resource, source");
    expect(dialog.textContent).not.toContain('{"text"');
    expect(dialog.querySelector("input:not([type=checkbox])")).toBeNull();
  });

  it.each([
    { name: "date", type: PropertyType.Date, value: "2026-09-14T12:34:56", expected: "2026-09-14" },
    { name: "time", type: PropertyType.Time, value: "01:02:03", expected: "01:02:03" },
  ])(
    "converts API $name values before passing them to the standard renderer",
    async ({ type, value, expected }) => {
      const dialog = await openPopover(propertyOf(type), [
        { scope: PropertyValueScope.Manual, bizValue: value },
      ]);

      expect(dialog).toHaveTextContent(expected);
      expect(dialog.querySelector("input:not([type=checkbox])")).toBeNull();
    },
  );
});
