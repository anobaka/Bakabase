import { useState } from "react";
import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import Fields, {
  defaultDeliveryContentLayout,
  deliveryContentLayoutsEqual,
  deliveryContentLayoutIsValid,
} from "../DeliveryContentLayoutFields";

vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onPress, isDisabled }: any) => (
    <button disabled={isDisabled} onClick={onPress}>
      {children}
    </button>
  ),
  Checkbox: ({ children, isSelected, onValueChange, isDisabled }: any) => (
    <label>
      <input
        type="checkbox"
        checked={isSelected}
        disabled={isDisabled}
        onChange={(e) => onValueChange(e.target.checked)}
      />
      {children}
    </label>
  ),
}));

describe("shared delivery layout policy", () => {
  it("does not upgrade a missing legacy policy until the user requests it", () => {
    const change = vi.fn();
    render(<Fields onChange={change} />);
    expect(screen.getByText("artifact.deliveryLayout.legacy")).toBeVisible();
    expect(screen.queryByRole("checkbox")).toBeNull();
    expect(change).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "artifact.deliveryLayout.configure" }));
    expect(change).toHaveBeenCalledExactlyOnceWith(defaultDeliveryContentLayout());
  });

  it("previews one optional author layer without flattening its inner tree", () => {
    const Component = () => {
      const [value, setValue] = useState(defaultDeliveryContentLayout());
      return <Fields value={value} onChange={setValue} />;
    };
    render(<Component />);
    expect(screen.getByText("Author folder/images/01.jpg")).toBeVisible();
    fireEvent.click(
      screen.getByRole("checkbox", { name: "artifact.deliveryLayout.collapseSingleRoot" }),
    );
    expect(screen.getByText("images/01.jpg")).toBeVisible();
    expect(screen.queryByText("01.jpg")).toBeNull();
    fireEvent.click(
      screen.getByRole("checkbox", { name: "artifact.deliveryLayout.removeGenerated" }),
    );
    expect(screen.getAllByText("bakabase-example/Author folder/images/01.jpg")).toHaveLength(2);
  });

  it("preserves future policy fields during edits", () => {
    const change = vi.fn();
    const policy = { ...defaultDeliveryContentLayout(), futureRule: "preserved" };
    render(<Fields value={policy} onChange={change} />);
    fireEvent.click(
      screen.getByRole("checkbox", { name: "artifact.deliveryLayout.collapseSingleRoot" }),
    );
    expect(change).toHaveBeenCalledWith({ ...policy, collapseSingleRootDirectory: true });
  });

  it("does not conflate an explicit keep policy with missing legacy configuration", () => {
    expect(deliveryContentLayoutsEqual(null, undefined)).toBe(true);
    expect(deliveryContentLayoutsEqual(null, defaultDeliveryContentLayout())).toBe(false);
    expect(
      deliveryContentLayoutsEqual(
        { version: 1, removeGeneratedDirectories: false, collapseSingleRootDirectory: false },
        defaultDeliveryContentLayout(),
      ),
    ).toBe(false);
    expect(deliveryContentLayoutIsValid(null)).toBe(true);
    expect(deliveryContentLayoutIsValid({ removeGeneratedDirectories: "true" })).toBe(false);
    expect(deliveryContentLayoutIsValid({ ...defaultDeliveryContentLayout(), version: 2 })).toBe(
      false,
    );
    expect(
      deliveryContentLayoutsEqual(
        { ...defaultDeliveryContentLayout(), version: 2 },
        defaultDeliveryContentLayout(),
      ),
    ).toBe(false);
  });
});
