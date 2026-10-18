import type { IProperty } from "@/components/Property/models";

import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import CoverPropertyValue from "../components/CoverPropertyValue";

import { PropertyPool, PropertyType, StandardValueType } from "@/sdk/constants";

const { createPortal } = vi.hoisted(() => ({ createPortal: vi.fn() }));

vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal }),
}));
vi.mock("@/hooks/useFilterOptionsThreshold", () => ({
  DEFAULT_FILTER_OPTIONS_THRESHOLD: 30,
  getFilterOptionsThreshold: () => 30,
  useFilterOptionsThreshold: () => [30],
}));
vi.mock("@/components/bakaui", () => ({
  Button: ({ children }: any) => <button>{children}</button>,
  Chip: ({ children }: any) => <span>{children}</span>,
  Input: () => <input />,
  Modal: () => null,
  Tooltip: ({ children }: any) => children,
}));

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

const definition = (type: PropertyType, options: any): IProperty =>
  ({ id: 17, pool: PropertyPool.Custom, type, options }) as IProperty;

describe("reference property colors on resource covers", () => {
  it("uses the exact single-choice option color and preserves the displayed alias", () => {
    const property = definition(PropertyType.SingleChoice, {
      choices: [
        { value: "choice", label: "Short", color: "#ff0000" },
        { value: "choice-long", label: "Long", color: "#0000ff" },
      ],
    });

    property.dbValueType = StandardValueType.String;
    render(
      <CoverPropertyValue
        dbValue="choice-long"
        definition={property}
        propertyType={property.type}
        type={StandardValueType.String}
        value="Display alias"
      />,
    );
    expect(screen.getByText("Display alias")).toHaveStyle({ color: "#0000ff" });
    fireEvent.click(screen.getByText("Display alias"));
    expect(createPortal).not.toHaveBeenCalled();
  });

  it("keeps multiple-choice colors aligned with server labels after a missing option is dropped", () => {
    const property = definition(PropertyType.MultipleChoice, {
      choices: [
        { value: "red", label: "Red", color: "#ff0000" },
        { value: "blue", label: "Blue", color: "#0000ff" },
      ],
    });

    property.dbValueType = StandardValueType.ListString;
    render(
      <CoverPropertyValue
        dbValue={["removed", "blue", "red"]}
        definition={property}
        propertyType={property.type}
        type={StandardValueType.ListString}
        value={["Blue", "Red"]}
      />,
    );
    expect(screen.getByText("Blue")).toHaveStyle({ color: "#0000ff" });
    expect(screen.getByText("Red")).toHaveStyle({ color: "#ff0000" });
    expect(screen.queryByText("removed")).not.toBeInTheDocument();
  });

  it("resolves tag colors by both group and name", () => {
    const property = definition(PropertyType.Tags, {
      tags: [
        { value: "red-tag", group: "Red group", name: "Same", color: "#ff0000" },
        { value: "blue-tag", group: "Blue group", name: "Same", color: "#0000ff" },
      ],
    });

    property.dbValueType = StandardValueType.ListString;
    render(
      <CoverPropertyValue
        dbValue={["blue-tag"]}
        definition={property}
        propertyType={property.type}
        type={StandardValueType.ListTag}
        value={[{ group: "Blue group", name: "Same" }]}
      />,
    );
    expect(screen.getByText("Blue group:Same")).toHaveStyle({ color: "#0000ff" });
  });

  it("uses the matched multilevel chain color", () => {
    const property = definition(PropertyType.Multilevel, {
      data: [
        {
          value: "red",
          label: "Red",
          children: [{ value: "red-leaf", label: "Leaf", color: "#ff0000" }],
        },
        {
          value: "blue",
          label: "Blue",
          children: [{ value: "blue-leaf", label: "Leaf", color: "#0000ff" }],
        },
      ],
    });

    property.dbValueType = StandardValueType.ListString;
    render(
      <CoverPropertyValue
        dbValue={["blue-leaf"]}
        definition={property}
        propertyType={property.type}
        type={StandardValueType.ListListString}
        value={[["Blue", "Leaf"]]}
      />,
    );
    expect(screen.getByText("Blue/Leaf")).toHaveStyle({ color: "#0000ff" });
  });

  it("keeps ordinary properties and reference values without metadata readable", () => {
    render(
      <>
        <CoverPropertyValue
          definition={definition(PropertyType.SingleLineText, {
            choices: [{ label: "Plain", color: "red" }],
          })}
          propertyType={PropertyType.SingleLineText}
          type={StandardValueType.String}
          value="Plain"
        />
        <CoverPropertyValue
          propertyType={PropertyType.SingleChoice}
          type={StandardValueType.String}
          value="Choice label"
        />
      </>,
    );
    expect(screen.getByText("Plain")).not.toHaveAttribute("style");
    expect(screen.getByText("Choice label")).toBeVisible();
  });
});
