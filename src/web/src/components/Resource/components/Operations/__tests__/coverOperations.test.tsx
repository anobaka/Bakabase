import type { CoverOperationsOptions } from "../coverOperations";

import { useState } from "react";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import CoverOperationsSettings from "../CoverOperationsSettings";
import { resolveCoverOperations } from "../coverOperations";

vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("@/components/bakaui", () => ({
  Switch: ({ children, isSelected, onValueChange }: any) => (
    <label>
      <input
        checked={isSelected}
        role="switch"
        type="checkbox"
        onChange={(event) => onValueChange(event.target.checked)}
      />
      {children}
    </label>
  ),
  Checkbox: ({ children, isSelected, onValueChange }: any) => (
    <label>
      <input
        checked={isSelected}
        type="checkbox"
        onChange={(event) => onValueChange(event.target.checked)}
      />
      {children}
    </label>
  ),
}));

afterEach(cleanup);

function Settings({
  initial,
  changed,
}: {
  initial?: CoverOperationsOptions;
  changed: (options: CoverOperationsOptions) => void;
}) {
  const [options, setOptions] = useState(initial);

  return (
    <CoverOperationsSettings
      options={options}
      onChange={(next) => {
        changed(next);
        setOptions(next);
      }}
    />
  );
}

const aggregate = () => screen.getByRole("switch", { name: "resource.display.aggregateButton" });
const pin = () => screen.getByRole("checkbox", { name: "resource.display.pinUnpin" });

describe("cover operation settings", () => {
  it.each([undefined, { displayOperations: [] }, { displayOperations: ["aggregate"] }])(
    "preserves legacy default aggregation and keeps it on after adding/removing the last shortcut: %j",
    (initial) => {
      const changed = vi.fn();

      render(<Settings changed={changed} initial={initial} />);
      expect(aggregate()).toBeChecked();
      expect(pin()).not.toBeChecked();
      fireEvent.click(pin());
      expect(aggregate()).toBeChecked();
      expect(changed).toHaveBeenLastCalledWith({
        showAggregateOperations: true,
        displayOperations: ["pin"],
      });
      fireEvent.click(pin());
      expect(aggregate()).toBeChecked();
      expect(changed).toHaveBeenLastCalledWith({
        showAggregateOperations: true,
        displayOperations: [],
      });
    },
  );

  it("does not reactivate a legacy direct-only menu when its last shortcut is removed", () => {
    const changed = vi.fn();

    render(<Settings changed={changed} initial={{ displayOperations: ["pin"] }} />);
    expect(aggregate()).not.toBeChecked();
    fireEvent.click(pin());
    expect(aggregate()).not.toBeChecked();
    expect(changed).toHaveBeenLastCalledWith({
      showAggregateOperations: false,
      displayOperations: [],
    });
  });

  it("toggles aggregation independently and preserves selected shortcuts", () => {
    const changed = vi.fn();

    render(<Settings changed={changed} initial={{ displayOperations: ["aggregate", "pin"] }} />);
    fireEvent.click(aggregate());
    expect(pin()).toBeChecked();
    expect(changed).toHaveBeenLastCalledWith({
      showAggregateOperations: false,
      displayOperations: ["pin"],
    });
    fireEvent.click(aggregate());
    expect(pin()).toBeChecked();
    expect(changed).toHaveBeenLastCalledWith({
      showAggregateOperations: true,
      displayOperations: ["pin"],
    });
  });

  it("can hide every operation, including refresh, and choose refresh as a direct shortcut", () => {
    const changed = vi.fn();

    render(
      <Settings
        changed={changed}
        initial={{ showAggregateOperations: false, displayOperations: [] }}
      />,
    );
    expect(aggregate()).not.toBeChecked();
    expect(
      screen.getAllByRole("checkbox").every((checkbox) => !(checkbox as HTMLInputElement).checked),
    ).toBe(true);
    fireEvent.click(screen.getByRole("checkbox", { name: "resource.action.refreshCache" }));
    expect(changed).toHaveBeenLastCalledWith({
      showAggregateOperations: false,
      displayOperations: ["refreshCache"],
    });
  });

  it("lets the explicit setting override a legacy sentinel without treating it as a shortcut", () => {
    expect(
      resolveCoverOperations({
        displayOperations: ["aggregate", "openFolder"],
        showAggregateOperations: false,
      }),
    ).toEqual({ showAggregate: false, directOperations: ["openFolder"] });
  });
});
