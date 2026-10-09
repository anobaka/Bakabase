import type { ReactNode } from "react";
import type { FileExplorerProps } from "../FileExplorer";

import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import FileExplorer from "../FileExplorer";

import { useFileExplorerClipboardStore } from "@/stores/fileExplorerClipboard";
import { useUserStorageStore } from "@/stores/userStorage";

const { checkPath, select, createPortal, moveEntries, getRoots, validatePaths } = vi.hoisted(
  () => ({
    checkPath: vi.fn().mockResolvedValue({ data: false }),
    select: vi.fn(),
    createPortal: vi.fn(),
    moveEntries: vi.fn().mockResolvedValue({}),
    getRoots: vi.fn(),
    validatePaths: vi.fn(),
  }),
);

vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, values?: { count: number; visible: number }) => {
      if (key === "fileExplorer.selection.total") return `${values?.count} items`;
      if (key === "fileExplorer.selection.filtered")
        return `${values?.visible} of ${values?.count} items`;

      return key;
    },
  }),
}));
vi.mock("@/sdk/BApi", () => ({
  default: {
    file: {
      checkPathIsFile: checkPath,
      moveEntries,
      getUserStorageRoots: getRoots,
      validateUserStoragePaths: validatePaths,
    },
  },
}));
vi.mock("@/components/utils", () => ({
  buildLogger: () => () => {},
  standardizePath: (path?: string) => path,
  getStandardParentPath: (path?: string) => (path?.includes("/") ? "/" : undefined),
  useTraceUpdate: () => {},
  createSelection: (input: HTMLInputElement, start: number, end: number) => {
    input.focus();
    input.setSelectionRange(start, end);
  },
  forceFocus: (element: HTMLElement) => element.focus(),
  getFileNameWithoutExtension: (name: string) => name.split(".")[0],
}));
vi.mock("auto-text-size", () => ({
  AutoTextSize: ({ children }: { children: ReactNode }) => <div>{children}</div>,
}));
vi.mock("@/core/models/FileExplorer/Entry", () => ({ Entry: class {} }));
vi.mock("@/core/models/FileExplorer/RootEntry", () => ({
  default: class {
    path?: string;
    children = ["alpha", "nas-bakabase", "nas-anobaka"].map((name) => ({
      path: `/media/${name}`,
      name,
      select,
    }));
    filter: { keyword?: string } = {};
    get filteredChildren() {
      return this.children.filter((entry) => entry.name.includes(this.filter.keyword ?? ""));
    }
    childrenCount = this.children.length;
    constructor(path?: string) {
      this.path = path;
      if (path === "/empty") this.children = [];
      this.childrenCount = this.children.length;
    }
    patchFilter(filter: { keyword?: string }) {
      this.filter = { ...this.filter, ...filter };
    }
    async dispose() {}
  },
}));
vi.mock("../FileExplorerEntry", async () => {
  const { useEffect, useReducer } = await import("react");
  const { default: EditableFileName } = await import("../components/EditableFileName");

  return {
    default: function MockEntry({ entry, onChildrenLoaded, switchSelective, filter }: any) {
      const [, forceUpdate] = useReducer((value) => value + 1, 0);

      useEffect(() => onChildrenLoaded(entry), [entry]);
      // Match the real child's effect timing: filtering its mutable model refreshes
      // the rows without rerendering the parent toolbar by itself.
      useEffect(() => {
        entry.patchFilter(filter);
        forceUpdate();
      }, [entry, filter]);

      return (
        <div data-filter={filter.keyword} data-testid="entries">
          {entry.filteredChildren.map((child: { path: string; name: string }) => (
            <div
              key={child.path}
              aria-label={`row ${child.name}`}
              className="entry-keydown-listener"
              role="button"
              tabIndex={0}
              onClick={(event) => {
                event.stopPropagation();
                switchSelective(child);
              }}
              onKeyDown={(event) => {
                if (event.key === "Enter" || event.key === " ") {
                  event.preventDefault();
                  switchSelective(child);
                }
              }}
            >
              <EditableFileName isDirectory name={child.name} path={child.path} />
            </div>
          ))}
        </div>
      );
    },
  };
});
vi.mock("../components/ContextMenu", () => ({
  default: ({ selectedEntries }: { selectedEntries: { name: string }[] }) => (
    <button role="menuitem">
      {selectedEntries.map((entry) => entry.name).join(",") || "working-directory"}
    </button>
  ),
}));
vi.mock("../components/DeleteConfirmationModal", () => ({ default: () => null }));
vi.mock("../components/WrapModal", () => ({ default: () => null }));
vi.mock("../components/ExtractModal", () => ({ default: () => null }));
vi.mock("@/components/FolderSelector", () => ({ default: () => null }));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal }),
}));
vi.mock("@/stores/remoteAccess", () => ({ useIsRemoteClient: () => true }));
vi.mock("@/stores/options", () => ({
  useFileSystemOptionsStore: (selector?: (state: object) => unknown) => {
    const state = { data: { showHiddenFiles: false }, patch: vi.fn() };

    return selector ? selector(state) : state;
  },
}));
vi.mock("@/components/bakaui", async () => {
  const { forwardRef } = await import("react");
  const { Button } = await import("@/components/bakaui/components/Button");

  return {
    Tooltip: ({ children }: { children: ReactNode }) => <>{children}</>,
    Chip: ({ children }: { children: ReactNode }) => <span>{children}</span>,
    Button,
    Kbd: ({ children }: { children: ReactNode }) => <kbd>{children}</kbd>,
    Modal: () => null,
    Input: forwardRef<HTMLInputElement, any>(
      ({ value, onValueChange, onKeyDown, onBlur, endContent, "aria-label": label }, ref) => (
        <div>
          <input
            ref={ref}
            aria-label={label}
            value={value ?? ""}
            onBlur={onBlur}
            onChange={(event) => onValueChange(event.target.value)}
            onKeyDown={onKeyDown}
          />
          {endContent}
        </div>
      ),
    ),
    toast: { success: vi.fn() },
  };
});

beforeEach(() => {
  vi.clearAllMocks();
  getRoots.mockResolvedValue({ data: { isRestricted: false, roots: [] } });
  validatePaths.mockResolvedValue({ code: 0 });
  useUserStorageStore.setState({ settings: undefined, error: undefined });
  useFileExplorerClipboardStore.getState().clear();
});
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe("compact file processor toolbar", () => {
  it("keeps Docker navigation inside a storage root and returns to storage locations without injecting slash", async () => {
    getRoots.mockResolvedValue({
      data: {
        isRestricted: true,
        roots: [{ path: "/media", name: "media", storageKind: "bind", readOnly: true }],
      },
    });
    const onInitialized = vi.fn();

    render(
      <FileExplorer
        appearance="compact"
        rootPath="/media"
        selectable="multiple"
        onInitialized={onInitialized}
      />,
    );
    await screen.findByRole("button", { name: "alpha" });
    expect(screen.getByRole("button", { name: "fileExplorer.navigation.parent" })).toBeDisabled();
    expect(validatePaths).toHaveBeenCalledWith({ paths: ["/media"] }, { showErrorToast: false });
    fireEvent.click(screen.getByRole("button", { name: "fileExplorer.storage.locations" }));
    await screen.findByText("fileExplorer.storage.help");
    expect(onInitialized).toHaveBeenLastCalledWith(undefined);
    expect(screen.getByRole("textbox", { name: "fileExplorer.navigation.path" })).toHaveValue("");
    expect(checkPath).not.toHaveBeenCalledWith({ path: "/" });
    // Read-only is informational: ordinary selection is still available.
    fireEvent.click(screen.getByRole("button", { name: "alpha" }));
    expect(screen.getByRole("button", { name: "fileExplorer.selection.clear" })).toBeEnabled();
  });

  it("shows a failed roots load and lets the user retry without exposing a filesystem fallback", async () => {
    getRoots.mockRejectedValueOnce(new Error("offline"));
    const onInitialized = vi.fn();

    render(<FileExplorer selectable="multiple" onInitialized={onInitialized} />);
    expect(await screen.findByRole("alert")).toHaveTextContent("fileExplorer.storage.loadFailed");
    expect(screen.queryByTestId("entries")).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "fileExplorer.storage.retry" }));
    await screen.findByRole("button", { name: "alpha" });
    expect(getRoots).toHaveBeenCalledTimes(2);
    expect(onInitialized).toHaveBeenCalledWith(undefined);
  });

  it("keeps compact controls opt-in for shared folder pickers", async () => {
    const view = render(<FileExplorer keyboard={false} rootPath="/media" selectable="multiple" />);

    await screen.findByRole("button", { name: "alpha" });
    expect(view.container.querySelector(".file-processor-explorer")).toBeNull();
    expect(screen.queryByRole("button", { name: "fileExplorer.selection.actions" })).toBeNull();
    expect(screen.getByText("fileExplorer.tip.pathsAreOnTheServer")).toBeInTheDocument();
  });

  it("clears the previous selection and action target when entering another directory", async () => {
    const onSelected = vi.fn();
    const props: FileExplorerProps = {
      appearance: "compact",
      capabilities: ["select", "create-directory"],
      selectable: "multiple",
      onSelected,
    };
    const view = render(<FileExplorer {...props} rootPath="/media" />);

    fireEvent.click(await screen.findByRole("button", { name: "alpha" }));
    expect(onSelected).toHaveBeenLastCalledWith([expect.objectContaining({ name: "alpha" })]);
    // A same-directory host render does not discard the current selection.
    view.rerender(<FileExplorer {...props} rootPath="/media" />);
    expect(screen.getByRole("button", { name: "fileExplorer.selection.clear" })).toBeEnabled();
    view.rerender(<FileExplorer {...props} rootPath="/empty" />);
    await screen.findByText("0 items");
    // The root renders before the selection's passive effect notifies its host.
    // Wait for that contract too, rather than assuming the row count implies it ran.
    await waitFor(() => expect(onSelected).toHaveBeenLastCalledWith([]));
    expect(screen.queryByRole("button", { name: "fileExplorer.selection.clear" })).toBeNull();
    expect(select).toHaveBeenCalledWith(false);
    fireEvent.click(screen.getByRole("button", { name: "fileExplorer.selection.actions" }));
    expect(await screen.findByRole("menuitem", { name: "working-directory" })).toBeInTheDocument();
    expect(screen.queryByRole("menuitem", { name: "alpha" })).toBeNull();
  });

  it("uses the host's compact notice and tools without duplicating the remote-path hint", async () => {
    render(
      <FileExplorer
        appearance="compact"
        keyboard={false}
        locationNotice={<span>Execution location</span>}
        rootPath="/media"
        selectable="multiple"
        toolbarContent={<button>AI tools</button>}
      />,
    );

    await screen.findByRole("button", { name: "alpha" });
    expect(screen.getByText("Execution location")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "AI tools" })).toBeInTheDocument();
    expect(screen.queryByText("fileExplorer.tip.pathsAreOnTheServer")).toBeNull();
    expect(screen.getByRole("textbox", { name: "fileExplorer.navigation.path" })).toHaveValue(
      "/media",
    );
  });

  it("opens actions for the current selection and clears it without losing the menu target", async () => {
    render(
      <FileExplorer
        appearance="compact"
        capabilities={["select", "create-directory"]}
        rootPath="/media"
        selectable="multiple"
      />,
    );
    fireEvent.click(await screen.findByRole("button", { name: "alpha" }));
    fireEvent.click(screen.getByRole("button", { name: "fileExplorer.selection.actions" }));

    expect(await screen.findByRole("menuitem", { name: "alpha" })).toBeInTheDocument();
    fireEvent.keyDown(screen.getByRole("menuitem", { name: "alpha" }), { key: "Escape" });
    fireEvent.click(screen.getByRole("button", { name: "fileExplorer.selection.clear" }));
    expect(select).toHaveBeenCalledWith(false);
    expect(screen.queryByRole("button", { name: "fileExplorer.selection.clear" })).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "fileExplorer.selection.actions" }));
    expect(await screen.findByRole("menuitem", { name: "working-directory" })).toBeInTheDocument();
  });

  it("lets the user filter names without triggering file-operation shortcuts", async () => {
    render(
      <FileExplorer
        appearance="compact"
        capabilities={["select", "wrap"]}
        rootPath="/media"
        selectable="multiple"
      />,
    );
    fireEvent.click(await screen.findByRole("button", { name: "alpha" }));
    const filter = screen.getByRole("textbox", { name: "fileExplorer.navigation.filter" });

    fireEvent.keyDown(filter, { key: "w" });
    fireEvent.change(filter, { target: { value: "w" } });
    expect(createPortal).not.toHaveBeenCalled();
    await waitFor(() => expect(screen.getByTestId("entries")).toHaveAttribute("data-filter", "w"));
    await act(async () => {});
  });

  it("keeps the count in sync with filtered rows, including no matches and clearing the filter", async () => {
    render(
      <FileExplorer
        appearance="compact"
        keyboard={false}
        rootPath="/media"
        selectable="multiple"
      />,
    );
    await screen.findByText("3 items");
    const filter = screen.getByRole("textbox", { name: "fileExplorer.navigation.filter" });

    fireEvent.change(filter, { target: { value: "nas-" } });
    await screen.findByText("2 of 3 items");
    expect(screen.queryByRole("button", { name: "alpha" })).toBeNull();
    expect(screen.getByRole("button", { name: "nas-bakabase" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "nas-anobaka" })).toBeInTheDocument();

    fireEvent.change(filter, { target: { value: "missing" } });
    await screen.findByText("0 of 3 items");
    expect(screen.getByTestId("entries")).toBeEmptyDOMElement();

    fireEvent.change(filter, { target: { value: "" } });
    await screen.findByText("3 items");
    expect(screen.getByRole("button", { name: "alpha" })).toBeInTheDocument();
  });

  it("preserves a host-provided filter and applies it after changing directories", async () => {
    const props = {
      appearance: "compact" as const,
      filter: { keyword: "nas-" },
      keyboard: false,
      selectable: "multiple" as const,
    };
    const view = render(<FileExplorer {...props} rootPath="/media" />);

    await screen.findByText("2 of 3 items");
    view.rerender(<FileExplorer {...props} rootPath="/other" />);
    await waitFor(() =>
      expect(screen.getByRole("textbox", { name: "fileExplorer.navigation.path" })).toHaveValue(
        "/other",
      ),
    );
    expect(screen.getByText("2 of 3 items")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "alpha" })).toBeNull();
  });

  it.each([
    ["MacIntel", "metaKey", "ctrlKey"],
    ["Win32", "ctrlKey", "metaKey"],
    ["Linux x86_64", "ctrlKey", "metaKey"],
  ])(
    "handles %s select/copy/cut/paste with the browser's primary key only",
    async (platform, primary, other) => {
      vi.stubGlobal("navigator", { platform });
      render(
        <FileExplorer
          appearance="compact"
          capabilities={["select", "wrap"]}
          rootPath="/media"
          selectable="multiple"
        />,
      );
      const fileName = await screen.findByRole("button", { name: "alpha" });

      fileName.focus();
      expect(fileName).toHaveFocus();
      fireEvent.keyDown(fileName, { key: "a", [other]: true });
      expect(select).not.toHaveBeenCalled();
      expect(fireEvent.keyDown(fileName, { key: "a", [primary]: true })).toBe(false);
      expect(select).toHaveBeenCalledTimes(3);
      fireEvent.keyDown(fileName, { key: "c", [primary]: true });
      expect(useFileExplorerClipboardStore.getState().mode).toBe("copy");
      expect(useFileExplorerClipboardStore.getState().paths).toHaveLength(3);
      fireEvent.keyDown(fileName, { key: "x", [primary]: true });
      expect(useFileExplorerClipboardStore.getState().mode).toBe("cut");
      fireEvent.click(screen.getByRole("button", { name: "fileExplorer.selection.clear" }));
      fileName.focus();
      fireEvent.keyDown(fileName, { key: "v", [primary]: true });
      fireEvent.keyDown(fileName, { key: "v", [primary]: true, repeat: true });
      await waitFor(() => expect(moveEntries).toHaveBeenCalledTimes(1));
      expect(moveEntries).toHaveBeenCalledWith({
        destDir: "/media",
        entryPaths: ["/media/alpha", "/media/nas-bakabase", "/media/nas-anobaka"],
      });

      fireEvent.click(screen.getByRole("button", { name: "alpha" }));
      expect(fireEvent.keyDown(fileName, { key: "w", [primary]: true })).toBe(true);
      expect(createPortal).not.toHaveBeenCalled();
      fireEvent.keyDown(fileName, { key: "w" });
      expect(createPortal).toHaveBeenCalledTimes(1);

      // Keep the real HeroUI button: its keyboard handling must not swallow
      // primary shortcuts merely because a toolbar control has focus.
      select.mockClear();
      const shortcuts = screen.getByRole("button", { name: "fileExplorer.label.shortcuts" });

      shortcuts.focus();
      expect(shortcuts).toHaveFocus();
      expect(fireEvent.keyDown(shortcuts, { key: "a", [primary]: true })).toBe(false);
      expect(select).toHaveBeenCalledTimes(3);
    },
  );

  it("protects actual filename editing while preserving row keyboard selection afterwards", async () => {
    vi.stubGlobal("navigator", { platform: "MacIntel" });
    render(
      <FileExplorer
        appearance="compact"
        capabilities={["select", "wrap", "delete"]}
        rootPath="/media"
        selectable="multiple"
      />,
    );
    const fileName = await screen.findByRole("button", { name: "alpha" });

    fileName.focus();
    fireEvent.click(fileName);
    fireEvent.keyDown(fileName, { key: "F2" });
    const input = screen.getByDisplayValue("alpha");

    expect(input).toHaveFocus();
    expect(input).toHaveRole("textbox");
    select.mockClear();
    for (const key of ["a", "c", "x", "v", "Backspace"]) {
      expect(fireEvent.keyDown(input, { key, metaKey: true })).toBe(true);
    }
    for (const key of ["w", "Delete", " "]) {
      expect(fireEvent.keyDown(input, { key })).toBe(true);
    }
    expect(select).not.toHaveBeenCalled();
    expect(createPortal).not.toHaveBeenCalled();
    expect(moveEntries).not.toHaveBeenCalled();
    expect(useFileExplorerClipboardStore.getState().paths).toEqual([]);
    expect(
      screen.getByRole("button", { name: "fileExplorer.selection.clear" }),
    ).toBeInTheDocument();
    fireEvent.keyDown(input, { key: "Escape" });
    expect(fileName).toHaveFocus();
    expect(fileName).toHaveRole("button");
    expect(select).not.toHaveBeenCalled();
    fireEvent.keyDown(fileName, { key: "Enter" });
    expect(screen.queryByRole("button", { name: "fileExplorer.selection.clear" })).toBeNull();

    fireEvent.keyDown(fileName, { key: "F2" });
    select.mockClear();
    fireEvent.keyDown(screen.getByDisplayValue("alpha"), { key: "Enter" });
    expect(fileName).toHaveFocus();
    expect(select).not.toHaveBeenCalled();
    expect(screen.queryByRole("button", { name: "fileExplorer.selection.clear" })).toBeNull();
  });
});
