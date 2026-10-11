import type { ComponentType, ReactNode } from "react";
import type { IProperty } from "@/components/Property/models";
import type { Resource } from "@/core/models/Resource";
import type * as Utils from "@/components/utils";

import { act, createElement, isValidElement } from "react";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import BulkPropertyEditor from "..";

import { getBizValueType, getDbValueType } from "@/components/Property/PropertySystem";
import { PropertyPool, PropertyType, PropertyValueScope } from "@/sdk/constants";

const { api, createPortal, translate } = vi.hoisted(() => ({
  api: {
    getProperties: vi.fn(),
    getCustomProperties: vi.fn(),
    bulkPut: vi.fn(),
  },
  createPortal: vi.fn(),
  translate: (key: string) => key,
}));

vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: translate }) }));
vi.mock("react-router-dom", () => ({ useNavigate: () => vi.fn() }));
vi.mock("@/sdk/BApi", () => ({
  default: {
    property: { getPropertiesByPool: api.getProperties },
    customProperty: { getAllCustomProperties: api.getCustomProperties },
    resource: { bulkPutResourcePropertyValue: api.bulkPut },
  },
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal }),
}));
vi.mock("@/components/Resource", () => ({ default: () => null }));
vi.mock("@/components/Chips/Property/BriefProperty", () => ({
  default: ({ property }: { property: IProperty }) => <span>{property.name}</span>,
}));
vi.mock("@/components/ResourceFilter/components/ParentResourceValueRenderer", () => ({
  default: () => null,
}));
vi.mock("@/components/utils", async (importOriginal) => ({
  ...(await importOriginal<typeof Utils>()),
  buildLogger: () => () => {},
}));
// Keep the actual property/value conversion and reference renderers; the barrel
// also loads unrelated attachment, media and filesystem UI.
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
}));
vi.mock("@/components/bakaui", () => {
  type ButtonProps = {
    children?: ReactNode;
    isDisabled?: boolean;
    onClick?: () => void;
    onPress?: () => void;
  };
  const Button = ({ children, isDisabled, onClick, onPress }: ButtonProps) => (
    <button disabled={isDisabled} type="button" onClick={onClick ?? onPress}>
      {children}
    </button>
  );

  return {
    Button,
    Chip: ({ children, isDisabled, onClick, color }: ButtonProps & { color?: string }) => (
      <button
        aria-pressed={color === "primary"}
        disabled={isDisabled}
        type="button"
        onClick={onClick}
      >
        {children}
      </button>
    ),
    Modal: ({
      children,
      footer,
      title,
      visible,
      onOk,
    }: {
      children?: ReactNode;
      footer?: ReactNode | { okProps?: { children?: ReactNode } };
      title?: string;
      visible?: boolean;
      onOk?: () => void;
    }) =>
      visible === false ? null : (
        <div aria-label={title} role="dialog">
          {children}
          {isValidElement(footer) && footer}
          {onOk && (
            <button type="button" onClick={onOk}>
              {(footer as { okProps?: { children?: ReactNode } })?.okProps?.children ?? "Apply"}
            </button>
          )}
        </div>
      ),
    Divider: () => <hr />,
    Radio: () => null,
    RadioGroup: () => null,
    Spinner: () => <span role="status">Saving</span>,
    Input: () => <input />,
    toast: { success: vi.fn(), error: vi.fn(), danger: vi.fn() },
  };
});

const scenarios = [
  { name: "single choice", type: PropertyType.SingleChoice, multiple: false },
  { name: "multiple choices", type: PropertyType.MultipleChoice, multiple: true },
  { name: "tags", type: PropertyType.Tags, multiple: true },
  { name: "multilevel choices", type: PropertyType.Multilevel, multiple: true },
];

function property(type: PropertyType): IProperty {
  const choices = [
    { value: "first-id", label: "First" },
    { value: "second-id", label: "Second" },
    { value: "old-id", label: "Old" },
  ];

  return {
    id: 1,
    pool: PropertyPool.Custom,
    type,
    name: "Review state",
    typeName: "Reference",
    poolName: "Custom",
    order: 0,
    dbValueType: getDbValueType(type),
    bizValueType: getBizValueType(type),
    options:
      type === PropertyType.Tags
        ? { tags: choices.map(({ value, label }) => ({ value, name: label })) }
        : type === PropertyType.Multilevel
          ? { data: choices }
          : { choices },
  };
}

async function renderEditor(
  type: PropertyType,
  values: Array<string[] | undefined> = [undefined, undefined],
) {
  const prop = property(type);
  const resources = values.map((value, index) => ({
    id: index + 1,
    fileName: `Resource ${index + 1}`,
    properties: {
      [PropertyPool.Custom]: {
        [prop.id]: {
          dbValueType: prop.dbValueType,
          bizValueType: prop.bizValueType,
          values: value
            ? [
                {
                  scope: PropertyValueScope.Manual,
                  value: type === PropertyType.SingleChoice ? value[0] : value,
                },
              ]
            : [],
        },
      },
    },
  })) as unknown as Resource[];

  api.getCustomProperties.mockResolvedValue({ data: [prop] });
  render(<BulkPropertyEditor initialResources={resources} resourceIds={[1, 2]} />);
  await screen.findByText(prop.name);
}

async function clickOption(label: string) {
  const option = await screen.findByRole("button", { name: label, exact: true });

  await act(async () => fireEvent.click(option));

  return option;
}

async function expectSaved(value?: string) {
  await waitFor(() =>
    expect(api.bulkPut).toHaveBeenLastCalledWith({
      resourceIds: [1, 2],
      propertyId: 1,
      isCustomProperty: true,
      value,
    }),
  );
}

beforeEach(() => {
  api.getProperties.mockReset().mockResolvedValue({ data: [] });
  api.getCustomProperties.mockReset();
  api.bulkPut.mockReset().mockResolvedValue({ code: 0 });
  createPortal
    .mockReset()
    .mockImplementation((component: ComponentType, props: Record<string, unknown>) => {
      render(createElement(component, props));
    });
});

afterEach(cleanup);

describe.each(scenarios)("bulk editing $name", ({ type, multiple }) => {
  it("preserves the inline editor while saving, adding, removing and clearing selections", async () => {
    let finishSave: (response: { code: number }) => void = () => {};

    api.bulkPut.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          finishSave = resolve;
        }),
    );
    await renderEditor(type);
    await act(async () =>
      fireEvent.click(screen.getByRole("button", { name: "common.label.clickToSet" })),
    );
    const secondOption = await screen.findByRole("button", { name: "Second", exact: true });

    await clickOption("First");
    await expectSaved("first-id");
    // Keeping the same option DOM node also checks that saving did not unmount
    // and replace the actual renderer with a loading indicator.
    expect(secondOption).toBeInTheDocument();
    await act(async () => finishSave({ code: 0 }));
    expect(screen.getByRole("button", { name: "Second", exact: true })).toBe(secondOption);
    expect(screen.getByRole("button", { name: "First", exact: true })).toHaveAttribute(
      "aria-pressed",
      "true",
    );

    await clickOption("Second");
    await expectSaved(multiple ? "first-id,second-id" : "second-id");
    if (multiple) {
      await clickOption("First");
      await expectSaved("second-id");
    }
    await clickOption("Second");
    await expectSaved(undefined);
    expect(screen.getByRole("button", { name: "First", exact: true })).toHaveAttribute(
      "aria-pressed",
      "false",
    );

    await clickOption("First");
    await expectSaved("first-id");
    expect(api.bulkPut).toHaveBeenCalledTimes(multiple ? 5 : 4);
  });

  it.each(["partial", "mixed"] as const)(
    "keeps editing after the first %s value change",
    async (state) => {
      let finishSave: (response: { code: number }) => void = () => {};

      api.bulkPut.mockImplementationOnce(
        () =>
          new Promise((resolve) => {
            finishSave = resolve;
          }),
      );
      await renderEditor(
        type,
        state === "partial" ? [["old-id"], undefined] : [["old-id"], ["second-id"]],
      );
      const status =
        state === "partial" ? "common.state.partiallySet" : "common.state.multipleValues";

      await act(async () => fireEvent.click(screen.getByRole("button", { name: status })));
      const secondOption = await screen.findByRole("button", { name: "Second", exact: true });

      await clickOption("First");
      if (state === "mixed") {
        expect(api.bulkPut).not.toHaveBeenCalled();
        const confirm = await screen.findByRole("button", { name: "common.action.confirm" });

        // The real confirmation dialog is outside the renderer's container.
        // Its mousedown must not close an explicitly opened bulk editor.
        await act(async () => {
          fireEvent.mouseDown(confirm);
          fireEvent.click(confirm);
        });
      }
      await expectSaved("first-id");
      expect(secondOption).toBeInTheDocument();
      await act(async () => finishSave({ code: 0 }));
      expect(screen.getByRole("button", { name: "Second", exact: true })).toBe(secondOption);
      await clickOption("Second");
      await expectSaved(multiple ? "first-id,second-id" : "second-id");
      expect(api.bulkPut).toHaveBeenCalledTimes(2);
      await act(async () => fireEvent.mouseDown(screen.getByText("Review state")));
      expect(screen.queryByRole("button", { name: "Old", exact: true })).not.toBeInTheDocument();
    },
  );
});
