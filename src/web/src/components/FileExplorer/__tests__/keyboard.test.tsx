import { useRef, type ReactNode } from "react";

import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import EventListener, { SelectionMode } from "../components/EventListener";
import Shortcuts from "../components/Shortcuts";

import ResourceShortcuts from "@/pages/resource/components/FilterPanel/ShortcutsButton";
import enFileExplorer from "@/locales/en/components/fileExplorer.json";
import enResource from "@/locales/en/pages/resource.json";

vi.mock("@/components/utils", () => ({ buildLogger: () => () => {} }));
vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, values: Record<string, string> = {}) => {
      const messages: Record<string, string> = { ...enFileExplorer, ...enResource };

      return (messages[key] ?? key).replace(/\{\{(\w+)\}\}/g, (_, name) => values[name] ?? "");
    },
  }),
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({
    createPortal: (_: unknown, props: { children: ReactNode }) => render(<>{props.children}</>),
  }),
}));
vi.mock("@/components/bakaui", () => ({
  Button: ({ onPress }: { onPress: () => void }) => <button onClick={onPress}>Shortcuts</button>,
  Tooltip: ({ children }: { children: ReactNode }) => <>{children}</>,
  Kbd: ({ children }: { children: ReactNode }) => <kbd>{children}</kbd>,
  Modal: () => null,
}));

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe("file explorer keyboard listener", () => {
  function ScopedExplorer({ name, onKeyDown, onDelete, children }: {
    name: string; onKeyDown: (key: string, event: KeyboardEvent) => void; onDelete?: () => boolean; children?: ReactNode;
  }) {
    const scopeRef = useRef<HTMLDivElement>(null);
    return <div ref={scopeRef} data-file-explorer-keyboard-scope="">
      <EventListener scopeRef={scopeRef} onKeyDown={onKeyDown} onDelete={onDelete} onSelectionModeChange={() => {}} />
      <div role="button" tabIndex={0}>{name}</div>{children}
    </div>;
  }

  it("handles the focused explorer in a dialog without invoking another explorer", () => {
    const main = vi.fn(), modal = vi.fn(), remove = vi.fn().mockReturnValue(true);
    render(<><ScopedExplorer name="Main files" onKeyDown={main} />
      <div role="dialog"><ScopedExplorer name="Modal files" onKeyDown={modal} onDelete={remove} /></div></>);
    const row = screen.getByRole("button", { name: "Modal files" });
    row.focus();
    fireEvent.keyDown(row, { key: "a", ctrlKey: true });
    expect(modal).toHaveBeenCalledWith("a", expect.any(KeyboardEvent));
    expect(main).not.toHaveBeenCalled();
    fireEvent.keyDown(row, { key: "Delete" });
    expect(remove).toHaveBeenCalledTimes(1);
    fireEvent.keyDown(document.body, { key: "Delete" });
    expect(remove).toHaveBeenCalledTimes(1);
  });

  it("leaves text editors, menus, nested dialogs, and handled or composing keys alone", () => {
    const key = vi.fn(), remove = vi.fn();
    render(<div role="dialog"><ScopedExplorer name="Files" onKeyDown={key} onDelete={remove}>
      <input aria-label="Rename" /><div contentEditable data-testid="content-editor" />
      <div role="menu"><button role="menuitem">Paste menu</button></div>
      <div role="dialog"><button>Confirmation</button></div>
      <button onKeyDown={event => event.preventDefault()}>Handled</button>
    </ScopedExplorer></div>);
    for (const target of [screen.getByRole("textbox"), screen.getByTestId("content-editor"),
      screen.getByRole("menuitem"), screen.getByText("Confirmation"), screen.getByText("Handled")]) {
      fireEvent.keyDown(target, { key: "Delete" }); fireEvent.keyDown(target, { key: "v", ctrlKey: true });
    }
    fireEvent.keyDown(screen.getByText("Files"), { key: "w", isComposing: true });
    fireEvent.keyDown(screen.getByText("Files"), { key: "w", repeat: true });
    expect(key).not.toHaveBeenCalled(); expect(remove).not.toHaveBeenCalled();
  });

  it("lets only the inner file explorer handle a nested file list", () => {
    const outer = vi.fn(), inner = vi.fn();
    render(<ScopedExplorer name="Outer files" onKeyDown={outer}>
      <ScopedExplorer name="Inner files" onKeyDown={inner} />
    </ScopedExplorer>);
    fireEvent.keyDown(screen.getByText("Inner files"), { key: "ArrowDown" });
    expect(inner).toHaveBeenCalledTimes(1); expect(outer).not.toHaveBeenCalled();
  });

  it.each([
    ["MacIntel", "metaKey", "ctrlKey"],
    ["Win32", "ctrlKey", "metaKey"],
    ["Linux x86_64", "ctrlKey", "metaKey"],
  ])("tracks only %s primary selection and resets on blur", (platform, primary, other) => {
    vi.stubGlobal("navigator", { platform });
    const changed = vi.fn();

    render(<EventListener onSelectionModeChange={changed} />);
    fireEvent.mouseDown(document.body, { [primary]: true });
    expect(changed).toHaveBeenLastCalledWith(SelectionMode.Ctrl);
    fireEvent.keyDown(window, { key: "Shift", shiftKey: true, [primary]: true });
    expect(changed).toHaveBeenLastCalledWith(SelectionMode.Shift);
    fireEvent.keyUp(window, { key: "Shift", [primary]: true });
    expect(changed).toHaveBeenLastCalledWith(SelectionMode.Ctrl);
    fireEvent.blur(window);
    expect(changed).toHaveBeenLastCalledWith(SelectionMode.Normal);
    changed.mockClear();
    fireEvent.mouseDown(document.body, { [other]: true });
    expect(changed).not.toHaveBeenCalled();
  });

  it("does not repeat operations or handle keys in editors/dialogs or during IME composition", () => {
    vi.stubGlobal("navigator", { platform: "MacIntel" });
    const remove = vi.fn().mockReturnValue(true);
    const key = vi.fn();

    render(
      <>
        <EventListener onDelete={remove} onKeyDown={key} onSelectionModeChange={() => {}} />
        <input aria-label="Name" />
        <div contentEditable data-testid="editor" />
        <div role="dialog">
          <button>Dialog action</button>
        </div>
      </>,
    );
    for (const target of [
      screen.getByRole("textbox"),
      screen.getByTestId("editor"),
      screen.getByRole("button"),
    ]) {
      fireEvent.keyDown(target, { key: "Delete" });
      fireEvent.keyDown(target, { key: "w" });
    }
    fireEvent.keyDown(window, { key: "w", isComposing: true });
    fireEvent.keyDown(window, { key: "w", repeat: true });
    expect(key).not.toHaveBeenCalled();
    expect(remove).not.toHaveBeenCalled();
    fireEvent.keyDown(window, { key: "Backspace" });
    expect(remove).not.toHaveBeenCalled();
    expect(fireEvent.keyDown(window, { key: "Backspace", metaKey: true })).toBe(false);
    fireEvent.keyDown(window, { key: "Backspace", metaKey: true, repeat: true });
    expect(remove).toHaveBeenCalledTimes(1);
    fireEvent.keyDown(window, { key: "ArrowDown", repeat: true });
    expect(key).toHaveBeenLastCalledWith("ArrowDown", expect.any(KeyboardEvent));
  });
});

describe("shortcut help uses the same browser keys", () => {
  it.each([
    ["MacIntel", "⌘", "⌘+⌫"],
    ["Win32", "Ctrl", "Delete"],
    ["Linux x86_64", "Ctrl", "Delete"],
  ])("shows %s file actions", (platform, modifier, remove) => {
    vi.stubGlobal("navigator", { platform });
    render(<Shortcuts capabilities={["multi-select", "delete"]} />);
    fireEvent.click(screen.getByRole("button"));
    expect(screen.getByText(`${modifier}+Click`)).toBeInTheDocument();
    for (const key of ["A", "C", "X", "V"])
      expect(screen.getByText(`${modifier}+${key}`)).toBeInTheDocument();
    expect(screen.getByText(remove, { selector: "kbd" })).toBeInTheDocument();
  });

  it("shows Command and Option gestures for Mac resource selection", () => {
    vi.stubGlobal("navigator", { platform: "MacIntel" });
    render(<ResourceShortcuts />);
    fireEvent.click(screen.getByRole("button"));
    expect(screen.getByText("⌘ + Click")).toBeInTheDocument();
    expect(screen.getByText("⌘ + Drag")).toBeInTheDocument();
    expect(screen.getByText("⌥ + Drag")).toBeInTheDocument();
  });
});
