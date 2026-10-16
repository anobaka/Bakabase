import type { IMenuItem } from "../AntdMenu/menuConfig";

import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { mergeNavigationLayout, type NavigationLayout } from "../AntdMenu/navigationLayout";

import NavigationEditor from ".";

import english from "@/locales/en/components/navigationEditor.json";

const drag = vi.hoisted(() => ({
  end: undefined as ((event: any) => void) | undefined,
  counter: 1,
}));
const labels: Record<string, string> = {
  "nav.resources": "Resources",
  "nav.tools": "Tools",
  "nav.processor": "File processor",
  "nav.rename": "File renamer",
  "nav.connections": "Devices",
  "nav.sync": "Data sync",
};
const translate = (key: string, values: Record<string, unknown> = {}) => {
  const text = (english as Record<string, string>)[key] ?? labels[key] ?? key;

  return text.replace(/\{\{(\w+)\}\}/g, (_all, name: string) => String(values[name] ?? ""));
};

vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: translate }) }));
vi.mock("uuid", () => ({ v4: () => `custom-directory-${drag.counter++}` }));
vi.mock("@dnd-kit/core", () => ({
  DndContext: ({ children, onDragEnd }: any) => {
    drag.end = onDragEnd;

    return children;
  },
  closestCenter: vi.fn(),
  KeyboardSensor: vi.fn(),
  PointerSensor: vi.fn(),
  useSensor: vi.fn(),
  useSensors: vi.fn(),
}));
vi.mock("@dnd-kit/sortable", () => ({
  SortableContext: ({ children }: any) => children,
  sortableKeyboardCoordinates: vi.fn(),
  verticalListSortingStrategy: vi.fn(),
  useSortable: () => ({
    attributes: {},
    listeners: {},
    setNodeRef: vi.fn(),
    setActivatorNodeRef: vi.fn(),
  }),
}));
vi.mock("@/components/bakaui", async () => {
  const { forwardRef } = await import("react");

  return {
    Modal: ({ children, footer, title, visible, onClose, hideCloseButton }: any) =>
      visible ? (
        <div aria-label={title} role="dialog">
          {!hideCloseButton && (
            <button aria-label="Close dialog" onClick={onClose}>
              ×
            </button>
          )}
          {children}
          {footer}
        </div>
      ) : null,
    Button: ({ children, onPress, isDisabled, isLoading, startContent, ...props }: any) => (
      <button aria-label={props["aria-label"]} disabled={isDisabled || isLoading} onClick={onPress}>
        {startContent}
        {children}
      </button>
    ),
    Input: forwardRef<HTMLInputElement, any>(
      (
        {
          value,
          onValueChange,
          isDisabled,
          isInvalid,
          errorMessage,
          maxLength,
          onFocus,
          ...props
        }: any,
        ref,
      ) => (
        <label>
          <input
            ref={ref}
            aria-invalid={isInvalid}
            aria-label={props["aria-label"]}
            disabled={isDisabled}
            maxLength={maxLength}
            value={value}
            onChange={(event) => onValueChange(event.target.value)}
            onFocus={onFocus}
          />
          {isInvalid && <span role="alert">{errorMessage}</span>}
        </label>
      ),
    ),
    Select: ({ dataSource, selectedKeys, onSelectionChange, isDisabled, ...props }: any) => (
      <select
        aria-label={props["aria-label"]}
        disabled={isDisabled}
        value={[...selectedKeys][0]}
        onChange={(event) => onSelectionChange(new Set([event.target.value]))}
      >
        {dataSource.map((entry: any) => (
          <option key={entry.value} value={entry.value}>
            {entry.label}
          </option>
        ))}
      </select>
    ),
    Chip: ({ children }: any) => <span>{children}</span>,
    Tooltip: ({ children }: any) => children,
  };
});

const catalog: readonly IMenuItem[] = [
  { name: "nav.resources", path: "/resource" },
  {
    name: "nav.tools",
    children: [
      { name: "nav.processor", path: "/processor" },
      { name: "nav.rename", path: "/rename" },
    ],
  },
  { name: "nav.connections", localNodeOnly: true, children: [{ name: "nav.sync", path: "/sync" }] },
];
const capabilities = { isPureClient: false, isLocalNode: true };
const initial = () => mergeNavigationLayout(catalog, undefined, translate);
const row = (id: string) =>
  document.querySelector(`[data-navigation-editor-node="${id}"]`)! as HTMLElement;
const saved = (save: ReturnType<typeof vi.fn>): NavigationLayout => save.mock.lastCall![0];
const show = (
  save = vi.fn().mockResolvedValue(undefined),
  close = vi.fn(),
  layout: unknown = undefined,
  caps = capabilities,
) => ({
  save,
  close,
  ...render(
    <NavigationEditor
      isOpen
      capabilities={caps}
      catalog={catalog}
      savedLayout={layout}
      onClose={close}
      onSave={save}
    />,
  ),
});

beforeEach(() => {
  vi.clearAllMocks();
  drag.counter = 1;
});
afterEach(cleanup);

describe("navigation editor", () => {
  it("keeps fixed feature names and unavailable features visible for editing", () => {
    show(undefined, undefined, undefined, { isPureClient: true, isLocalNode: false });
    expect(screen.getByText("Resources")).toBeInTheDocument();
    expect(screen.getByText("Data sync")).toBeInTheDocument();
    expect(within(row("menu:nav.sync")).getByText("Unavailable here")).toBeInTheDocument();
    expect(within(row("menu:nav.resources")).queryByRole("textbox")).toBeNull();
    expect(screen.getAllByRole("textbox")).toHaveLength(2);
  });

  it("moves and hides without freezing untouched built-in names in the current UI language", async () => {
    const { save } = show();

    fireEvent.click(screen.getByRole("button", { name: "Move Resources down" }));
    fireEvent.click(screen.getByRole("button", { name: "Hide File renamer" }));
    await act(async () => fireEvent.click(screen.getByRole("button", { name: "Save" })));
    expect(saved(save).items.map((node) => node.id)).toEqual([
      "directory:nav.tools",
      "menu:nav.resources",
      "directory:nav.connections",
    ]);
    expect(
      saved(save)
        .items.filter((node) => node.kind === "directory")
        .every((node) => node.name === undefined),
    ).toBe(true);
    expect(
      saved(save).items[0]!.children!.find((node) => node.id === "menu:nav.rename")!.hidden,
    ).toBe(true);
  });

  it("saves only explicitly edited directory name overrides", async () => {
    const { save } = show();

    fireEvent.change(screen.getByRole("textbox", { name: "Directory name: Tools" }), {
      target: { value: "  My utilities  " },
    });
    await act(async () => fireEvent.click(screen.getByRole("button", { name: "Save" })));
    expect(saved(save).items.find((node) => node.id === "directory:nav.tools")!.name).toBe(
      "My utilities",
    );
    expect(
      saved(save).items.find((node) => node.id === "directory:nav.connections")!.name,
    ).toBeUndefined();
  });

  it("supports a new directory, moving a feature into it, and promoting it back to the top level", async () => {
    const { save } = show();

    fireEvent.click(screen.getByRole("button", { name: "New directory" }));
    const input = screen.getByRole("textbox", { name: "Directory name: New directory" });

    expect(input).toHaveFocus();
    fireEvent.change(input, { target: { value: "Favorites" } });
    fireEvent.change(screen.getByRole("combobox", { name: "Location of Resources" }), {
      target: { value: "custom-directory-1" },
    });
    expect(within(row("custom-directory-1")).getByText("Resources")).toBeInTheDocument();
    fireEvent.change(screen.getByRole("combobox", { name: "Location of File processor" }), {
      target: { value: "navigation-editor-root" },
    });
    await act(async () => fireEvent.click(screen.getByRole("button", { name: "Save" })));
    const directory = saved(save).items.find((node) => node.id === "custom-directory-1")!;

    expect(directory.name).toBe("Favorites");
    expect(directory.children!.map((node) => node.id)).toEqual(["menu:nav.resources"]);
    expect(saved(save).items.some((node) => node.id === "menu:nav.processor")).toBe(true);
  });

  it("removes a directory by promoting its features in place, retaining their hidden state", async () => {
    const { save } = show();

    fireEvent.click(screen.getByRole("button", { name: "Hide File renamer" }));
    fireEvent.click(screen.getByRole("button", { name: "Remove directory Tools" }));
    expect(screen.getByText("File processor")).toBeInTheDocument();
    expect(screen.getByText("File renamer")).toBeInTheDocument();
    await act(async () => fireEvent.click(screen.getByRole("button", { name: "Save" })));
    expect(saved(save).items.map((node) => node.id)).toEqual([
      "menu:nav.resources",
      "menu:nav.processor",
      "menu:nav.rename",
      "directory:nav.connections",
    ]);
    expect(saved(save).items.find((node) => node.id === "menu:nav.rename")!.hidden).toBe(true);
  });

  it("offers disabled boundary buttons and keyboard-friendly ordering within a directory", async () => {
    const { save } = show();

    expect(screen.getByRole("button", { name: "Move Resources up" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Move File renamer down" })).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "Move File renamer up" }));
    await act(async () => fireEvent.click(screen.getByRole("button", { name: "Save" })));
    expect(saved(save).items[1]!.children!.map((node) => node.id)).toEqual([
      "menu:nav.rename",
      "menu:nav.processor",
    ]);
  });

  it("supports drag sorting and moving a feature onto an empty directory", async () => {
    const { save } = show();

    fireEvent.click(screen.getByRole("button", { name: "New directory" }));
    act(() =>
      drag.end!({ active: { id: "menu:nav.resources" }, over: { id: "custom-directory-1" } }),
    );
    act(() =>
      drag.end!({
        active: { id: "directory:nav.connections" },
        over: { id: "directory:nav.tools" },
      }),
    );
    await act(async () => fireEvent.click(screen.getByRole("button", { name: "Save" })));
    expect(saved(save).items[0]!.id).toBe("directory:nav.connections");
    expect(
      saved(save)
        .items.find((node) => node.id === "custom-directory-1")!
        .children!.map((node) => node.id),
    ).toEqual(["menu:nav.resources"]);
  });

  it("retains the draft on save failure and closes only after a successful retry", async () => {
    const save = vi.fn().mockRejectedValueOnce(new Error("offline")).mockResolvedValue(undefined);
    const { close } = show(save);

    fireEvent.click(screen.getByRole("button", { name: "Hide Resources" }));
    await act(async () => fireEvent.click(screen.getByRole("button", { name: "Save" })));
    expect(screen.getByRole("alert")).toHaveTextContent("Your changes are still here");
    expect(screen.getByRole("button", { name: "Show Resources" })).toBeInTheDocument();
    expect(close).not.toHaveBeenCalled();
    await act(async () => fireEvent.click(screen.getByRole("button", { name: "Save" })));
    expect(close).toHaveBeenCalledOnce();
    expect(saved(save).items[0]!.hidden).toBe(true);
  });

  it("retains editing while a newer saved layout arrives, and reloads that saved layout when reopened", () => {
    const save = vi.fn().mockResolvedValue(undefined);
    const close = vi.fn();
    const props = { catalog, capabilities, onClose: close, onSave: save };
    const { rerender } = render(<NavigationEditor {...props} isOpen />);

    fireEvent.change(screen.getByRole("textbox", { name: "Directory name: Tools" }), {
      target: { value: "My draft" },
    });
    const incoming = initial();

    incoming.items[0]!.hidden = true;
    rerender(<NavigationEditor {...props} isOpen savedLayout={incoming} />);
    expect(screen.getByRole("textbox", { name: "Directory name: My draft" })).toHaveValue(
      "My draft",
    );
    expect(screen.getByRole("button", { name: "Hide Resources" })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(save).not.toHaveBeenCalled();
    rerender(<NavigationEditor {...props} isOpen={false} savedLayout={incoming} />);
    rerender(<NavigationEditor {...props} isOpen savedLayout={incoming} />);
    expect(screen.getByRole("textbox", { name: "Directory name: Tools" })).toHaveValue("Tools");
    expect(screen.getByRole("button", { name: "Show Resources" })).toBeInTheDocument();
  });

  it("restores defaults in the draft without saving prematurely or creating directory name overrides", async () => {
    const customized = initial();

    customized.items[1]!.name = "Custom tools";
    customized.items[0]!.hidden = true;
    const { save } = show(undefined, undefined, customized);

    fireEvent.click(screen.getByRole("button", { name: "Restore defaults" }));
    expect(save).not.toHaveBeenCalled();
    expect(screen.getByRole("textbox", { name: "Directory name: Tools" })).toHaveValue("Tools");
    await act(async () => fireEvent.click(screen.getByRole("button", { name: "Save" })));
    expect(saved(save)).toEqual(initial());
  });

  it("rejects blank directory names and guards all editing while a save is pending", async () => {
    let finish!: () => void;
    const save = vi.fn(
      () =>
        new Promise<void>((resolve) => {
          finish = resolve;
        }),
    );

    show(save);
    fireEvent.change(screen.getByRole("textbox", { name: "Directory name: Tools" }), {
      target: { value: "   " },
    });
    expect(screen.getByRole("button", { name: "Save" })).toBeDisabled();
    expect(screen.getByRole("alert")).toHaveTextContent("1–80");
    fireEvent.change(screen.getAllByRole("textbox")[0]!, { target: { value: "Utilities" } });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() => expect(screen.getByRole("button", { name: "Cancel" })).toBeDisabled());
    expect(screen.getByRole("button", { name: "New directory" })).toBeDisabled();
    expect(screen.getByRole("textbox", { name: "Directory name: Utilities" })).toBeDisabled();
    expect(screen.queryByRole("button", { name: "Close dialog" })).toBeNull();
    await act(async () => finish());
  });
});
