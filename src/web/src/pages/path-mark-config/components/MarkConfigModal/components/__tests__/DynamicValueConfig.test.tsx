import type { ReactElement, ReactNode } from "react";
import type { MarkConfig } from "../../types";

import { Children, cloneElement, useState } from "react";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import DynamicValueConfig from "../DynamicValueConfig";
import { hasValidValueExtractor } from "../../utils";

import {
  PathMarkType,
  PathMarkValueBasePath,
  PathMatchMode,
  PropertyValueType,
} from "@/sdk/constants";
import translations from "@/locales/en/pages/pathMarkConfig.json";

vi.mock("@/components/bakaui", () => ({
  RadioGroup: ({
    children,
    label,
    value,
    onValueChange,
  }: {
    children: ReactElement[];
    label: string;
    value: string;
    onValueChange: (value: string) => void;
  }) => (
    <fieldset>
      <legend>{label}</legend>
      {Children.map(children, (child) =>
        cloneElement(child, {
          checked: child.props.value === value,
          onChange: () => onValueChange(child.props.value),
        }),
      )}
    </fieldset>
  ),
  Radio: ({
    children,
    value,
    checked,
    onChange,
  }: {
    children: ReactNode;
    value: string;
    checked: boolean;
    onChange: () => void;
  }) => (
    <label>
      <input checked={checked} type="radio" value={value} onChange={onChange} />
      {children}
    </label>
  ),
  NumberInput: ({
    label,
    value,
    onValueChange,
    isInvalid,
    errorMessage,
    description,
  }: {
    label: string;
    value?: number;
    onValueChange: (value: number) => void;
    isInvalid: boolean;
    errorMessage: string;
    description: string;
  }) => (
    <div>
      <label>
        {label}
        <input
          aria-invalid={isInvalid}
          type="number"
          value={value ?? ""}
          onChange={(event) =>
            onValueChange(event.target.value === "" ? Number.NaN : Number(event.target.value))
          }
        />
      </label>
      <p>{description}</p>
      {isInvalid && <p role="alert">{errorMessage}</p>}
    </div>
  ),
  Input: ({
    label,
    value,
    onValueChange,
  }: {
    label: string;
    value: string;
    onValueChange: (value: string) => void;
  }) => (
    <label>
      {label}
      <input value={value} onChange={(event) => onValueChange(event.target.value)} />
    </label>
  ),
  Button: ({ children, onPress }: { children: ReactNode; onPress: () => void }) => (
    <button onClick={onPress}>{children}</button>
  ),
}));

const t = (key: string) => (translations as Record<string, string>)[key] ?? key;
const Harness = ({ markType, initial }: { markType: PathMarkType; initial: MarkConfig }) => {
  const [config, setConfig] = useState(initial);

  return (
    <>
      <DynamicValueConfig
        config={config}
        markType={markType}
        t={t}
        updateConfig={(update) => setConfig((current) => ({ ...current, ...update }))}
      />
      <button disabled={!hasValidValueExtractor(config, markType)}>Save</button>
      <output data-testid="config">{JSON.stringify(config)}</output>
    </>
  );
};

const initial: MarkConfig = {
  matchMode: PathMatchMode.Layer,
  layer: 1,
  valueType: PropertyValueType.Dynamic,
  mediaLibraryValueType: PropertyValueType.Dynamic,
  valueBasePath: PathMarkValueBasePath.MarkPath,
  valueMatchMode: PathMatchMode.Layer,
  valueLayer: 1,
  layerToMediaLibrary: 1,
};

afterEach(cleanup);

describe.each([PathMarkType.Property, PathMarkType.MediaLibrary])(
  "DynamicValueConfig (type %s)",
  (markType) => {
    it("keeps a positive value when changing anchors, blocks save and offers an explicit correction", () => {
      render(<Harness initial={initial} markType={markType} />);
      fireEvent.click(screen.getByLabelText("Each resource"));
      expect(screen.getByRole("spinbutton")).toHaveValue(1);
      expect(screen.getByRole("alert")).toHaveTextContent(
        "positive value layers are not supported",
      );
      expect(screen.getByRole("button", { name: "Save" })).toBeDisabled();
      expect(JSON.parse(screen.getByTestId("config").textContent!)).toMatchObject({
        matchMode: PathMatchMode.Layer,
        layer: 1,
      });

      fireEvent.click(screen.getByRole("button", { name: "Use -1: parent directory" }));
      expect(screen.getByRole("spinbutton")).toHaveValue(-1);
      expect(screen.getByRole("button", { name: "Save" })).toBeEnabled();
    });

    it("allows regex extraction independently of matching and ignores an inactive positive layer", () => {
      render(
        <Harness
          initial={{ ...initial, valueBasePath: PathMarkValueBasePath.ResourcePath }}
          markType={markType}
        />,
      );
      fireEvent.click(screen.getByLabelText("Extract with regex"));
      fireEvent.change(screen.getByLabelText("Value Regex"), { target: { value: "(name)" } });
      expect(screen.queryByRole("spinbutton")).not.toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Save" })).toBeEnabled();
      expect(JSON.parse(screen.getByTestId("config").textContent!)).toMatchObject({
        matchMode: PathMatchMode.Layer,
        valueMatchMode: PathMatchMode.Regex,
      });
    });
  },
);

it("keeps migrated full-path regexes until the user explicitly converts them", () => {
  render(
    <Harness
      initial={{
        ...initial,
        valueMatchMode: PathMatchMode.Regex,
        valueRegex: "folder/(name)",
        valueRegexMatchesResourcePath: true,
      }}
      markType={PathMarkType.Property}
    />,
  );
  expect(screen.getByText("Preserving legacy full-path extraction")).toBeInTheDocument();
  expect(screen.queryByLabelText("Each resource")).not.toBeInTheDocument();
  expect(JSON.parse(screen.getByTestId("config").textContent!)).toMatchObject({
    valueRegexMatchesResourcePath: true,
  });

  fireEvent.click(screen.getByRole("button", { name: "Extract from the resource name instead" }));
  expect(screen.getByLabelText("Each resource")).toBeChecked();
  expect(JSON.parse(screen.getByTestId("config").textContent!)).toMatchObject({
    valueRegexMatchesResourcePath: false,
    valueBasePath: PathMarkValueBasePath.ResourcePath,
    valueMatchMode: PathMatchMode.Regex,
  });
});
