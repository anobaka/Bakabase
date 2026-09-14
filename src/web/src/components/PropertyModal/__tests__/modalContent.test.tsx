import { act } from "react-dom/test-utils";
import { createRoot } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";

import ModalContent from "../components/ModalContent";

import { PropertyPool, PropertyType } from "@/sdk/constants";

const state = vi.hoisted(() => ({
  choiceProps: undefined as any,
  tagProps: undefined as any,
  counts: vi.fn(),
  conversionRules: vi.fn(),
  createPortal: vi.fn(),
}));

vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("@/i18n", () => ({ getEnumKey: (_name: string, key: string) => key }));
vi.mock("@/sdk/BApi", () => ({
  default: {
    property: { getPropertyValueResourceCounts: state.counts },
    customProperty: { getCustomPropertyConversionRules: state.conversionRules },
  },
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: state.createPortal }),
}));
vi.mock("../components/ReferenceValueUsage", () => ({
  ReferenceValueUsageProvider: ({ children }: any) => children,
}));
vi.mock("../components/PropertyPreview", () => ({ default: () => <aside>preview</aside> }));
vi.mock("../components/ChoiceList", () => ({
  default: (props: any) => {
    state.choiceProps = props;

    return <div>choices</div>;
  },
}));
vi.mock("../components/TagList", () => ({
  default: (props: any) => {
    state.tagProps = props;

    return <div>tags</div>;
  },
}));
vi.mock("../components/MultilevelData", () => ({ default: () => <div>tree</div> }));
vi.mock("@/components/StandardValue/ValueRenderer", () => ({ default: () => null }));
vi.mock("@/components/Property/components/PropertyTypeIcon", () => ({
  default: ({ type }: any) => <span>icon-{type}</span>,
}));
vi.mock("@heroui/react", () => ({
  Radio: ({ children }: any) => children,
  RadioGroup: ({ children }: any) => children,
  TableHeader: ({ children }: any) => children,
}));
vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onPress, isDisabled, ...props }: any) => (
    <button
      aria-label={props["aria-label"]}
      aria-pressed={props["aria-pressed"]}
      disabled={isDisabled}
      onClick={onPress}
    >
      {children}
    </button>
  ),
  Input: ({ label, value, onValueChange }: any) => (
    <label>
      {label}
      <input value={value} onChange={(e) => onValueChange(e.target.value)} />
    </label>
  ),
  Switch: ({ children, isSelected, onValueChange }: any) => (
    <label>
      {children}
      <input
        checked={isSelected}
        type="checkbox"
        onChange={(e) => onValueChange(e.target.checked)}
      />
    </label>
  ),
  Select: ({ label, selectedKeys, selectionMode, dataSource = [], onSelectionChange }: any) => (
    <label>
      {label}
      <select
        multiple={selectionMode === "multiple"}
        value={selectionMode === "multiple" ? (selectedKeys ?? []) : (selectedKeys?.[0] ?? "")}
        onChange={(e) =>
          onSelectionChange(new Set([...e.target.selectedOptions].map((o) => o.value)))
        }
      >
        {dataSource.map((item: any) => (
          <option key={item.value} value={item.value}>
            {item.label}
          </option>
        ))}
      </select>
    </label>
  ),
  Popover: ({ trigger, visible, children }: any) => (
    <div>
      {trigger}
      {visible && children}
    </div>
  ),
  Chip: ({ children }: any) => children,
  Tooltip: ({ children }: any) => children,
  Modal: ({ children }: any) => children,
  Table: ({ children }: any) => children,
  TableBody: ({ children }: any) => children,
  TableCell: ({ children }: any) => children,
  TableColumn: ({ children }: any) => children,
  TableRow: ({ children }: any) => children,
}));

let container: HTMLDivElement;
let root: ReturnType<typeof createRoot>;
const changed = vi.fn();
const render = async (
  type: PropertyType,
  options: any = {},
  id?: number,
  validValueTypes?: PropertyType[],
) => {
  await act(async () =>
    root.render(
      <ModalContent
        validValueTypes={validValueTypes}
        value={{ id, name: "Example", type, options }}
        onChange={changed}
      />,
    ),
  );
};
const rename = async (name: string) => {
  const input = container.querySelector('input:not([type="checkbox"])')!;

  await act(async () => {
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(input, name);
    input.dispatchEvent(new Event("input", { bubbles: true }));
  });
};
const latest = () => changed.mock.calls.at(-1)?.[0];

beforeEach(() => {
  vi.clearAllMocks();
  state.choiceProps = undefined;
  state.tagProps = undefined;
  container = document.createElement("div");
  document.body.append(container);
  root = createRoot(container);
  (globalThis as any).IS_REACT_ACT_ENVIRONMENT = true;
});
afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
});

it("normalizes a cloned draft without mutating the supplied options", async () => {
  const options = Object.freeze({
    choices: Object.freeze([Object.freeze({ value: "a", label: "  Original  " })]),
    defaultValue: "a",
  });

  await render(PropertyType.SingleChoice, options, 7);
  expect(options.choices[0].label).toBe("  Original  ");
  expect(state.choiceProps.choices[0].label).toBe("Original");
  await rename("Renamed");
  expect(latest().name).toBe("Renamed");
  expect(latest().options.defaultValue).toBe("a");
});

it("preserves a multi-choice default array and removes only deleted defaults", async () => {
  await render(PropertyType.MultipleChoice, {
    choices: [
      { value: "a", label: "A" },
      { value: "b", label: "B" },
    ],
    defaultValue: ["a", "b"],
  });
  await rename("Renamed");
  expect(latest().options.defaultValue).toEqual(["a", "b"]);
  await act(async () => state.choiceProps.onChange([{ value: "b", label: "B" }]));
  expect(latest().options.defaultValue).toEqual(["b"]);
});

it("clears a single-choice default when its option is removed", async () => {
  await render(PropertyType.SingleChoice, {
    choices: [{ value: "a", label: "A" }],
    defaultValue: "a",
  });
  await act(async () => state.choiceProps.onChange([]));
  expect(latest().options.defaultValue).toBeUndefined();
});

it.each(["showProgressBar", "showProgressbar"])(
  "reads %s and writes the canonical percentage field",
  async (field) => {
    await render(PropertyType.Percentage, { [field]: true, precision: 2 });
    const checkbox = container.querySelector<HTMLInputElement>('input[type="checkbox"]')!;

    expect(checkbox.checked).toBe(true);
    await act(async () => checkbox.click());
    expect(latest().options).toEqual({
      showProgressBar: false,
      precision: 2,
      choices: [],
      tags: [],
    });
  },
);

it.each([PropertyType.MultipleChoice, PropertyType.Tags])(
  "checks exact UUID counts for type %s before deletion",
  async (type) => {
    state.counts.mockResolvedValue({ code: 0, data: { isReady: true, counts: { a: 6 } } });
    await render(type, {}, 7);
    const checkUsage = (type === PropertyType.Tags ? state.tagProps : state.choiceProps).checkUsage;

    expect(await checkUsage("a")).toBe(6);
    expect(state.counts).toHaveBeenCalledWith(PropertyPool.Custom, 7, { page: 1, pageSize: 100 });
    expect(await checkUsage("new")).toBe(0);
  },
);

it.each([
  { code: 1 },
  { code: 0, data: { isReady: false, counts: {} } },
  { code: 0, data: undefined },
])("does not treat unavailable resource counts as zero", async (response) => {
  state.counts.mockResolvedValue(response);
  await render(PropertyType.Tags, {}, 7);
  await expect(state.tagProps.checkUsage("a")).rejects.toThrow("property.editor.usageUnavailable");
});

it("does not start destructive conversion for the current type and respects restrictions", async () => {
  await render(PropertyType.SingleChoice, {}, 7, [
    PropertyType.SingleChoice,
    PropertyType.MultipleChoice,
  ]);
  await act(async () =>
    container
      .querySelector<HTMLButtonElement>('button[aria-label="property.editor.chooseType"]')!
      .click(),
  );
  const selected = container.querySelector<HTMLButtonElement>('button[aria-pressed="true"]')!;

  expect(selected.disabled).toBe(false);
  const formula = [...container.querySelectorAll("button")].find((b) =>
    b.textContent?.includes("property.editor.typeDescription.Formula"),
  )!;

  expect(formula.disabled).toBe(true);
  const number = [...container.querySelectorAll("button")].find((b) =>
    b.textContent?.includes("property.editor.typeDescription.Number"),
  )!;

  expect(number.disabled).toBe(true);
  await act(async () => selected.click());
  expect(state.conversionRules).not.toHaveBeenCalled();
  expect(state.createPortal).not.toHaveBeenCalled();
});

it("does not enable saving a whitespace-only name", async () => {
  await render(PropertyType.SingleLineText);
  await rename("   ");
  expect(changed).toHaveBeenCalledWith(undefined);
});
