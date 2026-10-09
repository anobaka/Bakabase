import type { ReactNode } from "react";

import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import FileExplorer from "../FileExplorer";

import { useFileExplorerClipboardStore } from "@/stores/fileExplorerClipboard";

const { checkPath, select, createPortal, moveEntries } = vi.hoisted(() => ({
  checkPath: vi.fn().mockResolvedValue({ data: false }),
  select: vi.fn(),
  createPortal: vi.fn(),
  moveEntries: vi.fn().mockResolvedValue({}),
}));

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
vi.mock("@/sdk/BApi", () => ({ default: { file: { checkPathIsFile: checkPath, moveEntries } } }));
vi.mock("@/components/utils", () => ({
  buildLogger: () => () => {},
  standardizePath: (path?: string) => path,
  getStandardParentPath: (path?: string) => (path?.includes("/") ? "/" : undefined),
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
    }
    patchFilter(filter: { keyword?: string }) {
      this.filter = { ...this.filter, ...filter };
    }
    async dispose() {}
  },
}));
vi.mock("../FileExplorerEntry", async () => {
  const { useEffect, useReducer } = await import("react");

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
            <button
              key={child.path}
              onClick={(event) => {
                event.stopPropagation();
                switchSelective(child);
              }}
            >
              {child.name}
            </button>
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
vi.mock("../components/Shortcuts", () => ({ default: () => null }));
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
vi.mock("@/components/bakaui", () => ({
  Tooltip: ({ children }: { children: ReactNode }) => <>{children}</>,
  Chip: ({ children }: { children: ReactNode }) => <span>{children}</span>,
  Button: ({
    children,
    isDisabled,
    onClick,
    onPress,
    "aria-label": label,
    "aria-haspopup": popup,
  }: any) => (
    <button
      aria-haspopup={popup}
      aria-label={label}
      disabled={isDisabled}
      onClick={onClick ?? onPress}
    >
      {children}
    </button>
  ),
  Input: ({ value, onValueChange, onKeyDown, endContent, "aria-label": label }: any) => (
    <div>
      <input
        aria-label={label}
        value={value ?? ""}
        onChange={(event) => onValueChange(event.target.value)}
        onKeyDown={onKeyDown}
      />
      {endContent}
    </div>
  ),
  toast: { success: vi.fn() },
}));

beforeEach(() => {
  vi.clearAllMocks();
  useFileExplorerClipboardStore.getState().clear();
});
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe("compact file processor toolbar", () => {
  it("keeps compact controls opt-in for shared folder pickers", async () => {
    const view = render(<FileExplorer keyboard={false} rootPath="/media" selectable="multiple" />);

    await screen.findByRole("button", { name: "alpha" });
    expect(view.container.querySelector(".file-processor-explorer")).toBeNull();
    expect(screen.queryByRole("button", { name: "fileExplorer.selection.actions" })).toBeNull();
    expect(screen.getByText("fileExplorer.tip.pathsAreOnTheServer")).toBeInTheDocument();
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
      await screen.findByRole("button", { name: "alpha" });
      fireEvent.keyDown(document.body, { key: "a", [other]: true });
      expect(select).not.toHaveBeenCalled();
      expect(fireEvent.keyDown(document.body, { key: "a", [primary]: true })).toBe(false);
      expect(select).toHaveBeenCalledTimes(3);
      fireEvent.keyDown(document.body, { key: "c", [primary]: true });
      expect(useFileExplorerClipboardStore.getState().mode).toBe("copy");
      expect(useFileExplorerClipboardStore.getState().paths).toHaveLength(3);
      fireEvent.keyDown(document.body, { key: "x", [primary]: true });
      expect(useFileExplorerClipboardStore.getState().mode).toBe("cut");
      fireEvent.click(screen.getByRole("button", { name: "fileExplorer.selection.clear" }));
      fireEvent.keyDown(document.body, { key: "v", [primary]: true });
      fireEvent.keyDown(document.body, { key: "v", [primary]: true, repeat: true });
      await waitFor(() => expect(moveEntries).toHaveBeenCalledTimes(1));
      expect(moveEntries).toHaveBeenCalledWith({
        destDir: "/media",
        entryPaths: ["/media/alpha", "/media/nas-bakabase", "/media/nas-anobaka"],
      });

      fireEvent.click(screen.getByRole("button", { name: "alpha" }));
      expect(fireEvent.keyDown(document.body, { key: "w", [primary]: true })).toBe(true);
      expect(createPortal).not.toHaveBeenCalled();
      fireEvent.keyDown(document.body, { key: "w" });
      expect(createPortal).toHaveBeenCalledTimes(1);
    },
  );
});
