import type { ReactNode } from "react";
import type { IProperty } from "@/components/Property/models";
import type * as Utils from "@/components/utils";
import type { MarkConfig } from "../../types";

import { useState } from "react";
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import FixedPropertyValueEditor from "../FixedPropertyValueEditor";
import PropertyMarkConfig from "../PropertyMarkConfig";

import { getBizValueType, getDbValueType } from "@/components/Property/PropertySystem";
import {
  deserializeStandardValue,
  serializeStandardValue,
} from "@/components/StandardValue/helpers";
import { PathMatchMode, PropertyPool, PropertyType, PropertyValueType } from "@/sdk/constants";

const { api, createPortal } = vi.hoisted(() => ({
  api: { getProperties: vi.fn(), getDbValue: vi.fn() },
  createPortal: vi.fn(),
}));
const t = (key: string) => key;

vi.mock("@/sdk/BApi", () => ({
  default: {
    property: { getPropertiesByPool: api.getProperties, getPropertyDbValue: api.getDbValue },
  },
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal }),
}));
vi.mock("@/components/PropertySelector", () => ({ default: () => null }));
vi.mock("../DynamicValueConfig", () => ({ default: () => <span>Dynamic editor</span> }));
vi.mock("@/components/ResourceFilter/components/ParentResourceValueRenderer", () => ({
  default: () => null,
}));
vi.mock("@/components/utils", async (importOriginal) => ({
  ...(await importOriginal<typeof Utils>()),
  buildLogger: () => () => {},
}));
// Keep real property serializers and reference value editors. The barrel also
// imports unrelated attachment/media controls that do not affect this flow.
vi.mock("@/components/StandardValue", async () => ({
  ...(await import("@/components/StandardValue/helpers")),
  ChoiceValueRenderer: (
    await import("@/components/StandardValue/ValueRenderer/Renderers/ChoiceValueRenderer")
  ).default,
  TagsValueRenderer: (
    await import("@/components/StandardValue/ValueRenderer/Renderers/TagsValueRenderer")
  ).default,
  MultilevelValueRenderer: (
    await import("@/components/StandardValue/ValueRenderer/Renderers/MultilevelValueRenderer")
  ).default,
  StringValueRenderer: (
    await import("@/components/StandardValue/ValueRenderer/Renderers/StringValueRenderer")
  ).default,
  NumberValueRenderer: (
    await import("@/components/StandardValue/ValueRenderer/Renderers/NumberValueRenderer")
  ).default,
  BooleanValueRenderer: (
    await import("@/components/StandardValue/ValueRenderer/Renderers/BooleanValueRenderer")
  ).default,
}));
vi.mock("@/components/bakaui", () => {
  type ButtonProps = {
    children?: ReactNode;
    onClick?: () => void;
    onPress?: () => void;
    isDisabled?: boolean;
  };

  return {
    Button: ({ children, onClick, onPress, isDisabled }: ButtonProps) => (
      <button disabled={isDisabled} type="button" onClick={onClick ?? onPress}>
        {children}
      </button>
    ),
    Chip: ({
      children,
      color,
      isDisabled,
      onClick,
      onClose,
    }: ButtonProps & { color?: string; onClose?: () => void }) => (
      <span>
        <button
          aria-pressed={color === "primary"}
          disabled={isDisabled}
          type="button"
          onClick={onClick}
        >
          {children}
        </button>
        {onClose && (
          <button aria-label="Clear property" type="button" onClick={onClose}>
            ×
          </button>
        )}
      </span>
    ),
    Input: ({
      value,
      onValueChange,
      type,
    }: {
      value?: string;
      type?: string;
      onValueChange?: (value: string) => void;
    }) => (
      <input type={type} value={value} onChange={(event) => onValueChange?.(event.target.value)} />
    ),
    Radio: () => null,
    RadioGroup: () => null,
  };
});

function property(type = PropertyType.SingleChoice, id = 1): IProperty {
  const choices = [
    { value: "first-id", label: "First, option" },
    { value: "second-id", label: "Second; option" },
  ];

  return {
    id,
    pool: PropertyPool.Custom,
    type,
    name: `Property ${id}`,
    dbValueType: getDbValueType(type),
    bizValueType: getBizValueType(type),
    typeName: "Type",
    poolName: "Custom",
    order: 0,
    options:
      type === PropertyType.Tags
        ? { tags: choices.map(({ value, label }) => ({ value, group: "Group", name: label })) }
        : type === PropertyType.Multilevel
          ? { data: [{ value: "parent-id", label: "Parent", children: choices }] }
          : { choices },
  };
}

function businessValue(type: PropertyType, label: string) {
  if (type === PropertyType.SingleChoice) return label;
  if (type === PropertyType.Tags) return [{ group: "Group", name: label }];
  if (type === PropertyType.Multilevel) return [["Parent", label]];

  return [label];
}

const referenceScenarios = [
  { name: "single choice", type: PropertyType.SingleChoice },
  { name: "multiple choice", type: PropertyType.MultipleChoice },
  { name: "tags", type: PropertyType.Tags },
  { name: "multilevel", type: PropertyType.Multilevel },
];

function Editor({
  property: prop,
  initialValue,
  onChange,
}: {
  property: IProperty;
  initialValue?: string | number | boolean;
  onChange: (value?: string) => void;
}) {
  const [value, setValue] = useState(initialValue);

  return (
    <FixedPropertyValueEditor
      property={prop}
      t={t}
      value={value}
      onChange={(next) => {
        setValue(next);
        onChange(next);
      }}
    />
  );
}

beforeEach(() => {
  api.getProperties.mockReset();
  api.getDbValue.mockReset();
  createPortal.mockReset();
});
afterEach(cleanup);

describe.each(referenceScenarios)("fixed $name property value", ({ type }) => {
  it("preselects by the converted database value and persists serialized business values on edit", async () => {
    const prop = property(type);
    const initialValue = serializeStandardValue(
      businessValue(type, "First, option"),
      prop.bizValueType,
    );
    const dbValue = serializeStandardValue(
      type === PropertyType.SingleChoice ? "first-id" : ["first-id"],
      prop.dbValueType,
    );
    const onChange = vi.fn();

    api.getDbValue.mockResolvedValue({ data: dbValue });
    render(<Editor initialValue={initialValue} property={prop} onChange={onChange} />);
    const firstLabel =
      type === PropertyType.Tags
        ? "Group:First, option"
        : type === PropertyType.Multilevel
          ? "Parent/First, option"
          : "First, option";
    const secondLabel =
      type === PropertyType.Tags
        ? "Group:Second; option"
        : type === PropertyType.Multilevel
          ? "Parent/Second; option"
          : "Second; option";
    const first = await screen.findByRole("button", { name: firstLabel, exact: true });

    expect(first).toHaveAttribute("aria-pressed", "true");
    expect(onChange).not.toHaveBeenCalled();
    expect(api.getDbValue).toHaveBeenCalledWith(
      prop.pool,
      prop.id,
      { bizValue: initialValue },
      expect.objectContaining({ signal: expect.any(AbortSignal) }),
    );

    fireEvent.click(first);
    await waitFor(() => expect(onChange).toHaveBeenLastCalledWith(undefined));
    fireEvent.click(screen.getByRole("button", { name: secondLabel, exact: true }));
    const serialized = onChange.mock.lastCall![0];

    expect(deserializeStandardValue(serialized, prop.bizValueType)).toEqual(
      businessValue(type, "Second; option"),
    );
    expect(screen.getByRole("button", { name: secondLabel, exact: true })).toHaveAttribute(
      "aria-pressed",
      "true",
    );
    expect(api.getDbValue).toHaveBeenCalledTimes(1);
  });
});

it("preserves an unmatched business value until the user selects an option", async () => {
  const prop = property();
  const onChange = vi.fn();

  api.getDbValue.mockResolvedValue({ data: null });
  render(<Editor initialValue="Unconfigured label" property={prop} onChange={onChange} />);
  await screen.findByRole("button", { name: "First, option", exact: true });
  expect(onChange).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("button", { name: "Second; option", exact: true }));
  expect(onChange).toHaveBeenLastCalledWith("Second; option");
});

it("does not infer a database ID from a GUID-shaped business label", async () => {
  const prop = property();
  const label = "d440837d-dc60-4802-8794-636f459de169";

  prop.options.choices = [
    { value: label, label: "Different label" },
    { value: "label-id", label },
  ];
  api.getDbValue.mockResolvedValue({ data: "label-id" });
  render(<Editor initialValue={label} property={prop} onChange={vi.fn()} />);
  expect(await screen.findByRole("button", { name: label, exact: true })).toHaveAttribute(
    "aria-pressed",
    "true",
  );
  expect(screen.getByRole("button", { name: "Different label", exact: true })).toHaveAttribute(
    "aria-pressed",
    "false",
  );
});

it("ignores an old conversion after switching properties", async () => {
  let resolveFirst: ((response: { data: string }) => void) | undefined;
  const first = property();
  const second = property(PropertyType.SingleChoice, 2);

  api.getDbValue.mockImplementation((_pool, id) =>
    id === 1
      ? new Promise((resolve) => {
          resolveFirst = resolve;
        })
      : Promise.resolve({ data: "second-id" }),
  );
  const view = render(
    <FixedPropertyValueEditor property={first} t={t} value="First, option" onChange={vi.fn()} />,
  );

  view.rerender(
    <FixedPropertyValueEditor property={second} t={t} value="Second; option" onChange={vi.fn()} />,
  );
  expect(
    await screen.findByRole("button", { name: "Second; option", exact: true }),
  ).toHaveAttribute("aria-pressed", "true");
  await act(async () => resolveFirst?.({ data: "first-id" }));
  expect(screen.getByRole("button", { name: "Second; option", exact: true })).toHaveAttribute(
    "aria-pressed",
    "true",
  );
});

it.each([
  { type: PropertyType.SingleLineText, value: "Fixed text", edited: "New text" },
  { type: PropertyType.Number, value: "12.5", edited: "15" },
])(
  "uses the nonreference property editor for type $type without conversion requests",
  async ({ type, value, edited }) => {
    const onChange = vi.fn();

    render(<Editor initialValue={value} property={property(type)} onChange={onChange} />);
    const input = screen.getByDisplayValue(value);

    fireEvent.change(input, { target: { value: edited } });
    expect(onChange).toHaveBeenLastCalledWith(edited);
    expect(api.getDbValue).not.toHaveBeenCalled();
  },
);

it("keeps a failed conversion editable through retry without changing the stored value", async () => {
  const onChange = vi.fn();

  api.getDbValue
    .mockRejectedValueOnce(new Error("offline"))
    .mockResolvedValueOnce({ data: "first-id" });
  render(<Editor initialValue="First, option" property={property()} onChange={onChange} />);
  expect(await screen.findByRole("alert")).toHaveTextContent("pathMarkConfig.error.loadFixedValue");
  fireEvent.click(screen.getByRole("button", { name: "common.action.retry" }));
  expect(await screen.findByRole("button", { name: "First, option", exact: true })).toHaveAttribute(
    "aria-pressed",
    "true",
  );
  expect(onChange).not.toHaveBeenCalled();
});

it("treats a nonzero conversion response code as an error without changing the fixed value", async () => {
  const onChange = vi.fn();

  api.getDbValue.mockResolvedValue({ code: 500, message: "Unavailable", data: "first-id" });
  render(<Editor initialValue="First, option" property={property()} onChange={onChange} />);
  expect(await screen.findByRole("alert")).toHaveTextContent("pathMarkConfig.error.loadFixedValue");
  expect(
    screen.queryByRole("button", { name: "First, option", exact: true }),
  ).not.toBeInTheDocument();
  expect(onChange).not.toHaveBeenCalled();
});

it.each([false, true])("emits multilevel label chains when singleton is %s", async (singleton) => {
  const prop = property(PropertyType.Multilevel);
  const onChange = vi.fn();
  const values = singleton ? ["first-id"] : ["first-id", "second-id"];

  prop.options.valueIsSingleton = singleton;
  api.getDbValue.mockResolvedValue({ data: serializeStandardValue(values, prop.dbValueType) });
  render(
    <Editor
      initialValue={serializeStandardValue(
        values.map((id) => ["Parent", id === "first-id" ? "First, option" : "Second; option"]),
        prop.bizValueType,
      )}
      property={prop}
      onChange={onChange}
    />,
  );
  await screen.findByRole("button", { name: "Parent/First, option", exact: true });
  fireEvent.click(
    screen.getByRole("button", {
      name: singleton ? "Parent/Second; option" : "Parent/First, option",
      exact: true,
    }),
  );
  expect(deserializeStandardValue(onChange.mock.lastCall![0], prop.bizValueType)).toEqual([
    ["Parent", "Second; option"],
  ]);
});

it("preserves legacy false and zero scalar fixed values on load", () => {
  const onChange = vi.fn();
  const view = render(
    <Editor initialValue={false} property={property(PropertyType.Boolean)} onChange={onChange} />,
  );

  expect(screen.getByRole("button", { name: "common.label.no" })).toHaveAttribute(
    "aria-pressed",
    "true",
  );
  expect(screen.getByRole("button", { name: "common.label.yes" })).toHaveAttribute(
    "aria-pressed",
    "false",
  );
  view.unmount();
  render(<Editor initialValue={0} property={property(PropertyType.Number)} onChange={onChange} />);
  expect(screen.getByDisplayValue("0")).toBeInTheDocument();
  expect(onChange).not.toHaveBeenCalled();
  expect(api.getDbValue).not.toHaveBeenCalled();
});

const initialConfig: MarkConfig = {
  matchMode: PathMatchMode.Layer,
  propertyPool: PropertyPool.Custom,
  propertyId: 1,
  valueType: PropertyValueType.Fixed,
  fixedValue: "First, option",
};

it("loads changed property definitions and ignores a stale catalog response", async () => {
  let resolveFirst: ((response: { data: IProperty[] }) => void) | undefined;
  const first = property();
  const second = property(PropertyType.SingleChoice, 2);

  api.getProperties
    .mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          resolveFirst = resolve;
        }),
    )
    .mockResolvedValue({ data: [second] });
  const view = render(<PropertyMarkConfig config={initialConfig} t={t} updateConfig={vi.fn()} />);

  view.rerender(
    <PropertyMarkConfig
      config={{ ...initialConfig, propertyId: 2, fixedValue: undefined }}
      t={t}
      updateConfig={vi.fn()}
    />,
  );
  await screen.findByRole("button", { name: second.name, exact: true });
  await act(async () => resolveFirst?.({ data: [first] }));
  expect(screen.queryByRole("button", { name: first.name, exact: true })).not.toBeInTheDocument();
  expect(screen.getByRole("button", { name: second.name, exact: true })).toBeInTheDocument();
});

it("clears the previous fixed value on a property switch or clear", async () => {
  const first = property();
  const second = property(PropertyType.SingleChoice, 2);
  const updateConfig = vi.fn();

  api.getProperties.mockResolvedValue({ data: [first] });
  api.getDbValue.mockResolvedValue({ data: "first-id" });
  const view = render(
    <PropertyMarkConfig config={initialConfig} t={t} updateConfig={updateConfig} />,
  );

  await screen.findByRole("button", { name: first.name, exact: true });
  fireEvent.click(screen.getByRole("button", { name: "common.action.change" }));
  await act(async () => createPortal.mock.lastCall![1].onSubmit([second]));
  expect(updateConfig).toHaveBeenLastCalledWith({
    propertyPool: second.pool,
    propertyId: second.id,
    fixedValue: undefined,
  });

  api.getProperties.mockResolvedValue({ data: [second] });
  view.rerender(
    <PropertyMarkConfig
      config={{ ...initialConfig, propertyId: 2, fixedValue: undefined }}
      t={t}
      updateConfig={updateConfig}
    />,
  );
  await screen.findByRole("button", { name: second.name, exact: true });
  fireEvent.click(screen.getByRole("button", { name: "Clear property" }));
  expect(updateConfig).toHaveBeenLastCalledWith({
    propertyPool: undefined,
    propertyId: undefined,
    fixedValue: undefined,
  });
});

it("reports catalog response errors and supports retry without clearing the configured value", async () => {
  const prop = property();
  const updateConfig = vi.fn();

  api.getProperties
    .mockResolvedValueOnce({ code: 500, message: "Unavailable", data: [prop] })
    .mockResolvedValueOnce({ data: [prop] });
  api.getDbValue.mockResolvedValue({ data: "first-id" });
  render(<PropertyMarkConfig config={initialConfig} t={t} updateConfig={updateConfig} />);
  expect(await screen.findByRole("alert")).toHaveTextContent("pathMarkConfig.error.loadProperty");
  fireEvent.click(screen.getByRole("button", { name: "common.action.retry" }));
  expect(await screen.findByRole("button", { name: prop.name, exact: true })).toBeInTheDocument();
  expect(updateConfig).not.toHaveBeenCalled();
});

it("guides the user to select a property before showing a fixed value editor", () => {
  render(
    <PropertyMarkConfig config={{ matchMode: PathMatchMode.Layer }} t={t} updateConfig={vi.fn()} />,
  );
  expect(screen.getByText("pathMarkConfig.hint.selectPropertyForFixedValue")).toBeInTheDocument();
  expect(screen.queryByRole("textbox")).not.toBeInTheDocument();
});
