import type { ComponentProps, ReactNode } from "react";
import type * as Utils from "@/components/utils";

import { forwardRef, useImperativeHandle } from "react";
import { act } from "react-dom/test-utils";
import { createRoot } from "react-dom/client";
import { HeroUIProvider } from "@heroui/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import PropertyModal from "..";

import ActualModal from "@/components/bakaui/components/Modal";
import ActualPopover from "@/components/bakaui/components/Popover";
import ActualSelect from "@/components/bakaui/components/Select";
import { PropertyPool, PropertyType } from "@/sdk/constants";

const { save, add, counts, createPortal, danger, saved } = vi.hoisted(() => ({
  save: vi.fn(),
  add: vi.fn(),
  counts: vi.fn(),
  createPortal: vi.fn(),
  danger: vi.fn(),
  saved: vi.fn(),
}));

vi.mock("@/i18n", () => ({ getEnumKey: (name: string, value: string) => `${name}.${value}` }));
vi.mock("@/sdk/BApi", () => ({
  default: {
    customProperty: { putCustomProperty: save, addCustomProperty: add },
    property: { getPropertyValueResourceCounts: counts },
  },
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal }),
}));
vi.mock("@/components/utils", async (importOriginal) => ({
  ...(await importOriginal<typeof Utils>()),
  buildLogger: () => () => {},
}));
vi.mock("@/hooks/useReferenceValueResourceCounts", () => ({
  useReferenceValueResourceCounts: () => ({ loading: true, refresh: () => {} }),
}));
vi.mock("../components/ReferenceValueResourcesModal", () => ({ default: () => null }));
vi.mock("../components/PropertyPreview", () => ({ default: () => null }));
vi.mock("../components/MultilevelData", () => ({ default: () => null }));
vi.mock("@/components/StandardValue/ValueRenderer", () => ({ default: () => null }));

// Supply layout measurements only. The production list, rows, deletion checks,
// form state, save/cancel footer and HeroUI selection controls remain in use.
vi.mock("react-virtualized", () => ({
  AutoSizer: ({ children }: { children: (size: { width: number }) => ReactNode }) =>
    children({ width: 640 }),
  List: forwardRef(function TestSizedList(
    {
      rowCount,
      rowRenderer,
    }: {
      rowCount: number;
      rowRenderer: (row: { index: number; style: object }) => ReactNode;
    },
    ref,
  ) {
    useImperativeHandle(ref, () => ({ scrollToRow: () => {} }));

    return (
      <div>{Array.from({ length: rowCount }, (_, index) => rowRenderer({ index, style: {} }))}</div>
    );
  }),
}));
vi.mock("@/components/bakaui", async () => ({
  ...(await import("@heroui/react")),
  Button: (await import("@/components/bakaui/components/Button")).Button,
  Modal: (props: ComponentProps<typeof ActualModal>) => <ActualModal {...props} />,
  Popover: (props: ComponentProps<typeof ActualPopover>) => <ActualPopover {...props} />,
  Select: (props: ComponentProps<typeof ActualSelect>) => <ActualSelect {...props} />,
  ColorPicker: ({ trigger }: { trigger: ReactNode }) => trigger,
  toast: { danger },
}));

const firstId = "4d4071d8-0206-4613-b549-84a9de39c777";
const secondId = "745df459-aa57-468d-8a85-1b0e423c748c";
const choices = [
  { value: firstId, label: "Adventure", color: "#cc2244" },
  { value: secondId, label: "Puzzle", color: "#2266aa" },
];

let container: HTMLDivElement;
let root: ReturnType<typeof createRoot>;

beforeEach(() => {
  vi.stubGlobal("IS_REACT_ACT_ENVIRONMENT", true);
  vi.clearAllMocks();
  counts.mockReset();
  counts.mockResolvedValue({ code: 0, data: { isReady: true, counts: {} } });
  save.mockResolvedValue({ code: 0, data: { id: 10 } });
  add.mockResolvedValue({ code: 0, data: { id: 11 } });
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
});

async function open(value: ComponentProps<typeof PropertyModal>["value"]) {
  await act(async () => {
    root.render(
      <HeroUIProvider disableAnimation>
        <PropertyModal value={value} onSaved={saved} />
      </HeroUIProvider>,
    );
  });

  return document.querySelector<HTMLElement>('[role="dialog"]')!;
}

function button(name: string, scope: ParentNode = document) {
  const found = Array.from(scope.querySelectorAll<HTMLButtonElement>("button")).find(
    (element) => element.getAttribute("aria-label") === name || element.textContent === name,
  );

  expect(found, `button ${name}`).toBeDefined();

  return found!;
}

async function click(element: HTMLElement) {
  await act(async () => element.click());
}

async function setInput(element: HTMLInputElement, value: string) {
  await act(async () => {
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(element, value);
    element.dispatchEvent(new Event("input", { bubbles: true }));
  });
}

function savedOptions() {
  expect(save).toHaveBeenCalledTimes(1);
  const [id, payload] = save.mock.lastCall!;

  expect(id).toBe(10);
  expect(typeof payload.options).toBe("string");

  return JSON.parse(payload.options);
}

describe("property configuration save and cancel", () => {
  it("saves multiple default choices selected through the real dropdown as UUIDs in a JSON array", async () => {
    const dialog = await open({
      id: 10,
      name: "Genres",
      type: PropertyType.MultipleChoice,
      options: { choices, defaultValue: [firstId] },
    });
    const trigger = dialog.querySelector<HTMLButtonElement>('button[aria-haspopup="listbox"]')!;

    expect(trigger).toHaveTextContent("Adventure");
    await click(trigger);
    const option = Array.from(document.querySelectorAll<HTMLElement>('[role="option"]')).find(
      (element) => element.textContent === "Puzzle",
    )!;

    expect(option).toBeDefined();
    await click(option);
    expect(trigger).toHaveTextContent("Adventure");
    expect(trigger).toHaveTextContent("Puzzle");
    await click(trigger);
    await click(button("property.editor.save", dialog));

    expect(savedOptions()).toMatchObject({
      defaultValue: [firstId, secondId],
      choices,
    });
    expect(save.mock.lastCall![1]).toMatchObject({
      name: "Genres",
      type: PropertyType.MultipleChoice,
    });
    expect(saved).toHaveBeenCalledExactlyOnceWith({ id: 10 });
    expect(add).not.toHaveBeenCalled();
    expect(document.querySelector('[role="dialog"]')).toBeNull();
  });

  it.each(["showProgressBar", "showProgressbar"])(
    "restores %s and saves the changed percentage switch with canonical wire casing",
    async (key) => {
      const dialog = await open({
        id: 10,
        name: "Progress",
        type: PropertyType.Percentage,
        options: { precision: 2, [key]: true },
      });
      const toggle = dialog.querySelector<HTMLInputElement>('input[type="checkbox"]')!;

      expect(toggle).toBeChecked();
      await click(toggle);
      expect(toggle).not.toBeChecked();
      await click(button("property.editor.save", dialog));

      expect(savedOptions()).toMatchObject({ precision: 2, showProgressBar: false });
      expect(savedOptions()).not.toHaveProperty("showProgressbar");
    },
  );

  it("cancels edits without changing the caller's original options or sending a save", async () => {
    const original = {
      id: 10,
      name: "Genres",
      type: PropertyType.MultipleChoice,
      options: {
        choices: [{ value: firstId, label: "  Adventure  ", color: "#cc2244" }],
        defaultValue: [firstId],
      },
    };
    const before = structuredClone(original);
    const dialog = await open(original);
    const input = dialog.querySelector<HTMLInputElement>(
      'input[aria-label="property.referenceEditor.choices.name"]',
    )!;

    expect(input).toHaveValue("Adventure");
    await setInput(input, "Edited draft");
    expect(input).toHaveValue("Edited draft");
    await click(button("property.editor.cancel", dialog));

    expect(original).toEqual(before);
    expect(save).not.toHaveBeenCalled();
    expect(add).not.toHaveBeenCalled();
    expect(saved).not.toHaveBeenCalled();
    expect(document.querySelector('[role="dialog"]')).toBeNull();
  });

  it("keeps an option until its usage is ready, and cleans its default after a successful retry deletes it", async () => {
    let resolveCounts!: (response: unknown) => void;

    counts.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          resolveCounts = resolve;
        }),
    );
    const dialog = await open({
      id: 10,
      name: "Genres",
      type: PropertyType.MultipleChoice,
      options: { choices: [choices[0]], defaultValue: [firstId] },
    });
    const deleteOption = button("property.referenceEditor.delete", dialog);

    await click(deleteOption);
    expect(counts).toHaveBeenCalledExactlyOnceWith(PropertyPool.Custom, 10, {
      page: 1,
      pageSize: 100,
    });
    expect(
      dialog.querySelector('input[aria-label="property.referenceEditor.choices.name"]'),
    ).toHaveValue("Adventure");
    expect(createPortal).not.toHaveBeenCalled();
    await act(async () =>
      resolveCounts({ code: 0, data: { isReady: false, counts: { [firstId]: 0 } } }),
    );
    expect(danger).toHaveBeenCalledExactlyOnceWith("property.referenceEditor.usageCheckFailed");
    expect(
      dialog.querySelector('input[aria-label="property.referenceEditor.choices.name"]'),
    ).toHaveValue("Adventure");
    expect(save).not.toHaveBeenCalled();

    counts.mockResolvedValueOnce({ code: 0, data: { isReady: true, counts: { [firstId]: 0 } } });
    await click(deleteOption);
    expect(
      dialog.querySelector('input[aria-label="property.referenceEditor.choices.name"]'),
    ).toBeNull();
    await click(button("property.editor.save", dialog));

    expect(savedOptions()).toMatchObject({ choices: [], defaultValue: [] });
  });
});

describe("case matching defaults for custom properties", () => {
  const referenceTypes = [
    PropertyType.SingleChoice,
    PropertyType.MultipleChoice,
    PropertyType.Tags,
    PropertyType.Multilevel,
  ].map((type) => ({ type, name: PropertyType[type] }));

  it.each(referenceTypes)(
    "checks ignore case after choosing $name for a new property and saves true",
    async ({ type }) => {
      const dialog = await open(undefined);

      await click(button("property.editor.chooseType", dialog));
      const typeButton = Array.from(
        document.querySelectorAll<HTMLButtonElement>("button[aria-pressed]"),
      ).find((element) => element.textContent?.includes(`PropertyType.${PropertyType[type]}`))!;

      expect(typeButton).toBeDefined();
      await click(typeButton);
      expect(dialog.querySelector('input[type="checkbox"]')).toBeChecked();
      // MultilevelData is outside this suite's scope; naming the property drives
      // the real form validation/save path without relying on an editor mount effect.
      await setInput(
        dialog.querySelector<HTMLInputElement>(
          'input[placeholder="property.editor.namePlaceholder"]',
        )!,
        "New property",
      );
      await click(button("property.editor.save", dialog));

      expect(add).toHaveBeenCalledTimes(1);
      const payload = add.mock.lastCall![0];

      expect(payload).toMatchObject({ name: "New property", type });
      expect(typeof payload.options).toBe("string");
      expect(JSON.parse(payload.options).ignoreCase).toBe(true);
      expect(save).not.toHaveBeenCalled();
    },
  );

  it("keeps an explicit unchecked choice when a new property is edited again and saved", async () => {
    const dialog = await open({ name: "New tags", type: PropertyType.Tags });
    const toggle = dialog.querySelector<HTMLInputElement>('input[type="checkbox"]')!;

    expect(toggle).toBeChecked();
    await click(toggle);
    expect(toggle).not.toBeChecked();
    await setInput(
      dialog.querySelector<HTMLInputElement>(
        'input[placeholder="property.editor.namePlaceholder"]',
      )!,
      "Case-sensitive tags",
    );
    expect(toggle).not.toBeChecked();
    await click(button("property.editor.save", dialog));

    expect(add).toHaveBeenCalledTimes(1);
    expect(JSON.parse(add.mock.lastCall![0].options).ignoreCase).toBe(false);
    expect(save).not.toHaveBeenCalled();
  });

  it.each([
    { name: "missing", ignoreCase: undefined },
    { name: "explicitly false", ignoreCase: false },
  ])(
    "leaves an existing property's $name ignore-case setting unchecked on save",
    async ({ ignoreCase }) => {
      const dialog = await open({
        id: 10,
        name: "Existing genres",
        type: PropertyType.MultipleChoice,
        options: { choices, ...(ignoreCase === undefined ? {} : { ignoreCase }) },
      });

      expect(dialog.querySelector('input[type="checkbox"]')).not.toBeChecked();
      await setInput(
        dialog.querySelector<HTMLInputElement>(
          'input[placeholder="property.editor.namePlaceholder"]',
        )!,
        "Renamed genres",
      );
      expect(dialog.querySelector('input[type="checkbox"]')).not.toBeChecked();
      await click(button("property.editor.save", dialog));

      expect(savedOptions().ignoreCase).toBe(ignoreCase);
      expect(add).not.toHaveBeenCalled();
    },
  );

  it("does not add an ignore-case option to a new numeric property", async () => {
    const dialog = await open({ name: "New number", type: PropertyType.Number });

    expect(dialog.querySelector('input[type="checkbox"]')).toBeNull();
    await setInput(
      dialog.querySelector<HTMLInputElement>(
        'input[placeholder="property.editor.namePlaceholder"]',
      )!,
      "Size",
    );
    await click(button("property.editor.save", dialog));

    expect(add).toHaveBeenCalledTimes(1);
    expect(JSON.parse(add.mock.lastCall![0].options)).not.toHaveProperty("ignoreCase");
  });
});
