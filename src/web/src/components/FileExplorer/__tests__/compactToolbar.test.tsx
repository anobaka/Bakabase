import type { ReactNode } from "react";

import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import FileExplorer from "../FileExplorer";

const { checkPath, select, createPortal } = vi.hoisted(() => ({
  checkPath: vi.fn().mockResolvedValue({ data: false }),
  select: vi.fn(),
  createPortal: vi.fn(),
}));

vi.mock("@/sdk/BApi", () => ({ default: { file: { checkPathIsFile: checkPath } } }));
vi.mock("@/components/utils", () => ({
  buildLogger: () => () => {},
  standardizePath: (path?: string) => path,
  getStandardParentPath: (path?: string) => (path?.includes("/") ? "/" : undefined),
}));
vi.mock("@/core/models/FileExplorer/Entry", () => ({ Entry: class {} }));
vi.mock("@/core/models/FileExplorer/RootEntry", () => ({
  default: class {
    path?: string;
    children = [{ path: "/media/alpha", name: "alpha", select }];
    filteredChildren = this.children;
    childrenCount = this.children.length;
    constructor(path?: string) {
      this.path = path;
    }
    patchFilter() {}
    async dispose() {}
  },
}));
vi.mock("../FileExplorerEntry", async () => {
  const { useEffect } = await import("react");

  return {
    default: function MockEntry({ entry, onChildrenLoaded, switchSelective, filter }: any) {
      useEffect(() => onChildrenLoaded(entry), [entry]);

      return (
        <div data-filter={filter.keyword} data-testid="entries">
          <button
            onClick={(event) => {
              event.stopPropagation();
              switchSelective(entry.children[0]);
            }}
          >
            alpha
          </button>
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

beforeEach(() => vi.clearAllMocks());
afterEach(cleanup);

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
});
