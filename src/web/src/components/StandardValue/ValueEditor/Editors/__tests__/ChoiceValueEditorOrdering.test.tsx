import type { ReactNode } from "react";

import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import ChoiceValueEditor from "../ChoiceValueEditor";
import ChoiceValueRenderer from "../../../ValueRenderer/Renderers/ChoiceValueRenderer";

import {
  createMediaLibraryOrderSource,
  useMediaLibraryOrder,
} from "@/features/management/useMediaLibraryOrder";
import { useUiOptionsStore } from "@/stores/options";

const mocks = vi.hoisted(() => ({ patch: vi.fn(), changed: vi.fn(), drag: vi.fn() }));

vi.mock("@/sdk/BApi", () => ({ default: { options: new Proxy({}, { get: () => mocks.patch }) } }));
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: vi.fn() }),
}));
vi.mock("@dnd-kit/core", async () => ({
  ...(await vi.importActual("@dnd-kit/core")),
  DndContext: ({ children, onDragEnd }: any) => {
    mocks.drag.mockImplementation(onDragEnd);

    return <>{children}</>;
  },
}));
vi.mock("@/components/bakaui", () => ({
  Modal: ({ children, onOk }: { children: ReactNode; onOk: () => void }) => (
    <div role="dialog">
      {children}
      <button onClick={onOk}>Confirm</button>
    </div>
  ),
  Input: ({ placeholder, value, onValueChange }: any) => (
    <input
      aria-label={placeholder}
      value={value}
      onChange={(event) => onValueChange(event.target.value)}
    />
  ),
  Button: ({ children, onClick, isDisabled, color }: any) => (
    <button aria-pressed={color === "primary"} disabled={isDisabled} onClick={onClick}>
      {children}
    </button>
  ),
}));
vi.mock("@/hooks/useFilterOptionsThreshold", () => ({
  useFilterOptionsThreshold: () => [2],
  DEFAULT_FILTER_OPTIONS_THRESHOLD: 2,
  getFilterOptionsThreshold: () => 2,
}));
vi.mock("@/components/StandardValue/ValueRenderer/Renderers/components/SelectableChip", () => ({
  default: ({ label, onClick, isSelected }: any) => (
    <button aria-pressed={isSelected} onClick={onClick}>
      {label}
    </button>
  ),
}));

const choices = [
  { value: "1", label: "Archive" },
  { value: "2", label: "Comics" },
  { value: "3", label: "Films" },
];
const entries = choices.map(({ value }) => ({ value, key: value }));
const source = createMediaLibraryOrderSource(entries);

function Editor({ orderable = true }: { orderable?: boolean }) {
  const order = useMediaLibraryOrder(choices.map(({ value }) => value));

  return (
    <ChoiceValueEditor
      multiple
      getDataSource={async () => choices}
      optionOrdering={
        orderable
          ? {
              source,
              dragLabel: (label) => `Arrange ${label}`,
              failureLabel: "Save failed",
              onMove: (active, over, visible) => order.move(active, over, visible),
            }
          : undefined
      }
      value={["2"]}
      onValueChange={mocks.changed}
    />
  );
}
const displayed = () =>
  screen
    .getAllByRole("button")
    .filter((button) => choices.some((choice) => button.textContent === choice.label))
    .map((button) => button.textContent);

beforeEach(() => {
  vi.clearAllMocks();
  mocks.patch.mockResolvedValue({ code: 0 });
  useUiOptionsStore.setState({
    data: { mediaLibraryOrder: { keys: ["1", "2", "3"] } },
    initialized: true,
  });
});
afterEach(cleanup);

describe("opt-in media-library ordering in an open choice editor", () => {
  it("reorders the entire inline catalog before applying the threshold without reloading or changing selection", async () => {
    const load = vi.fn(async () => choices);

    render(
      <ChoiceValueRenderer
        isEditing
        multiple
        editor={{ value: ["2"], onValueChange: mocks.changed }}
        getDataSource={load}
        optionOrdering={{
          source,
          dragLabel: (label) => `Arrange ${label}`,
          onMove: async () => true,
        }}
      />,
    );
    await waitFor(() => expect(displayed()).toEqual(["Archive", "Comics"]));
    act(() =>
      useUiOptionsStore.getState().update({ mediaLibraryOrder: { keys: ["3", "1", "2"] } }),
    );
    expect(displayed()).toEqual(["Films", "Comics"]);
    expect(screen.getByRole("button", { name: "Comics" })).toHaveAttribute("aria-pressed", "true");
    expect(load).toHaveBeenCalledOnce();
    expect(mocks.changed).not.toHaveBeenCalled();
  });

  it("follows another view's confirmed order without altering its selected IDs", async () => {
    render(<Editor />);
    await waitFor(() => expect(displayed()).toEqual(["Archive", "Comics", "Films"]));
    act(() =>
      useUiOptionsStore.getState().update({ mediaLibraryOrder: { keys: ["3", "2", "1"] } }),
    );
    expect(displayed()).toEqual(["Films", "Comics", "Archive"]);
    expect(screen.getByRole("button", { name: "Comics" })).toHaveAttribute("aria-pressed", "true");
    expect(mocks.changed).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "Confirm" }));
    expect(mocks.changed).toHaveBeenCalledWith(["2"], ["Comics"]);
  });

  it("persists drag while a search hides another library and leaves selection unchanged", async () => {
    render(<Editor />);
    await waitFor(() => expect(displayed()).toHaveLength(3));
    fireEvent.change(screen.getByRole("textbox"), { target: { value: "i" } });
    expect(displayed()).toEqual(["Archive", "Comics", "Films"]);
    fireEvent.change(screen.getByRole("textbox"), { target: { value: "m" } });
    expect(displayed()).toEqual(["Comics", "Films"]);
    await act(async () => mocks.drag({ active: { id: "3" }, over: { id: "2" } }));
    expect(mocks.patch).toHaveBeenLastCalledWith({ mediaLibraryOrder: { keys: ["1", "3", "2"] } });
    expect(displayed()).toEqual(["Films", "Comics"]);
    fireEvent.change(screen.getByRole("textbox"), { target: { value: "" } });
    expect(displayed()).toEqual(["Archive", "Films", "Comics"]);
    expect(mocks.changed).not.toHaveBeenCalled();
  });

  it("keeps normal property editors free of library-order controls", async () => {
    render(<Editor orderable={false} />);
    await waitFor(() => expect(displayed()).toHaveLength(3));
    expect(screen.queryByRole("button", { name: /^Arrange/ })).toBeNull();
    expect(mocks.patch).not.toHaveBeenCalled();
  });
});
