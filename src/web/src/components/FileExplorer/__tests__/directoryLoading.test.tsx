import type * as ComponentUtils from "@/components/utils";
import type { ReactNode } from "react";

import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import FileExplorerEntry from "../FileExplorerEntry";

import { pageChildren } from "@/core/models/FileExplorer/DirectoryPage";
import RootEntry from "@/core/models/FileExplorer/RootEntry";
import { Entry, EntryProperty } from "@/core/models/FileExplorer/Entry";
import { IwFsType } from "@/sdk/constants";
import { useUserStorageStore } from "@/stores/userStorage";

const mocks = vi.hoisted(() => ({
  children: vi.fn(),
  watch: vi.fn(),
  stop: vi.fn(),
  initialized: vi.fn(),
  move: vi.fn(),
  resizeObservers: [] as { targets: Set<Element>; notify: () => void }[],
  uuid: 0,
}));

vi.mock("@/sdk/BApi", () => ({
  default: {
    file: {
      getChildrenIwFsInfo: mocks.children,
      moveEntries: mocks.move,
      startWatchingChangesInFileProcessorWorkspace: mocks.watch,
      stopWatchingChangesInFileProcessorWorkspace: mocks.stop,
      keepAliveFileProcessorWatcher: vi.fn(),
      getIwFsInfo: vi.fn().mockResolvedValue({ data: { childrenCount: 0 } }),
    },
  },
}));
vi.mock("@/components/utils", async importOriginal => ({
  buildLogger: () => () => {},
  standardizePath: (path: string) => path,
  getStandardParentPath: (path: string) => path?.slice(0, path.lastIndexOf("/")),
  humanFileSize: (await importOriginal<typeof ComponentUtils>()).humanFileSize,
  uuidv4: () => String(++mocks.uuid),
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: vi.fn(), createWindow: vi.fn() }),
}));
vi.mock("@/components/MediaPlayer", () => ({ default: () => null }));
vi.mock("@/components/FileSystemEntryIcon", () => ({ default: () => null }));
vi.mock("../components/EditableFileName", async () => {
  const { forwardRef } = await import("react");

  return {
    default: forwardRef<HTMLSpanElement, { name: string; displayName?: string }>(
      ({ name, displayName }, ref) => <span ref={ref}>{displayName ?? name}</span>,
    ),
  };
});
vi.mock("../components/RightOperations", () => ({ default: () => null }));
vi.mock("../components/TailingOperations", () => ({ default: () => null }));
vi.mock("../components/LeftIcon", () => ({ default: () => null }));
vi.mock("../components/TaskOverlay", () => ({ default: () => null }));
vi.mock("../components/OperationButton", () => ({ default: () => null }));
vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onPress }: { children: ReactNode; onPress?: () => void }) => (
    <button onClick={onPress}>{children}</button>
  ),
  Chip: ({ children }: { children: ReactNode }) => <span>{children}</span>,
  Tooltip: ({ children }: { children: ReactNode }) => children,
  Modal: () => null,
  Spinner: () => <span>spinner</span>,
  toast: { danger: vi.fn(), success: vi.fn() },
}));
vi.mock("react-virtualized", async () => {
  const { forwardRef, useImperativeHandle } = await import("react");

  return {
    List: forwardRef(
      (
        {
          rowCount,
          rowRenderer,
          rowHeight,
          height,
          width,
        }: {
          width?: number;
          height: number;
          rowCount: number;
          rowHeight: (props: { index: number }) => number;
          rowRenderer: (props: object) => ReactNode;
        },
        ref,
      ) => {
        useImperativeHandle(ref, () => ({ recomputeRowHeights: vi.fn(), scrollToRow: vi.fn() }));
        const rowHeights = Array.from({ length: rowCount }, (_, index) => rowHeight({ index }));

        return (
          <div
            data-testid="directory-list"
            data-width={width}
            data-height={height}
            data-row-heights={JSON.stringify(rowHeights)}
          >
            {Array.from({ length: rowCount }, (_, index) =>
              rowRenderer({ index, style: { height: rowHeights[index], width } }),
            )}
          </div>
        );
      },
    ),
  };
});

const directory = (path: string, name: string) => ({ path, name, type: IwFsType.Directory });
const mount = (root: RootEntry) =>
  render(<FileExplorerEntry entry={root} onChildrenLoaded={mocks.initialized} />);

beforeEach(() => {
  vi.clearAllMocks();
  mocks.resizeObservers.length = 0;
  useUserStorageStore.setState({ settings: undefined });
  vi.stubGlobal(
    "ResizeObserver",
    class {
      private targets = new Set<Element>();

      constructor(callback: ResizeObserverCallback) {
        mocks.resizeObservers.push({
          targets: this.targets,
          notify: () => callback([], this as unknown as ResizeObserver),
        });
      }

      observe = vi.fn((target: Element) => this.targets.add(target));
      disconnect = vi.fn(() => this.targets.clear());
    },
  );
  mocks.stop.mockResolvedValue({ code: 0 });
  mocks.move.mockResolvedValue({ code: 0 });
  mocks.watch.mockResolvedValue({ code: 0 });
  vi.spyOn(HTMLElement.prototype, "clientHeight", "get").mockReturnValue(300);
  vi.spyOn(HTMLElement.prototype, "clientWidth", "get").mockReturnValue(500);
});
afterEach(() => {
  cleanup();
  useUserStorageStore.setState({ settings: undefined });
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe("directory listing initialization", () => {
  it.each([[0, "0 B"], [4096, "4.0 KiB"]])("shows the formatted size of a file including %s bytes", async (size, label) => {
    const root = new RootEntry("/files", false, false);
    root.children = [new Entry({ path: "/files/archive.zip", name: "archive.zip",
      type: IwFsType.CompressedFileEntry, size, parent: root, properties: [EntryProperty.Size] })];
    root.expireFilteredChildren();
    mount(root);
    expect(await screen.findByText(label)).toBeVisible();
  });
  it("labels Docker storage roots with their exact paths while keeping child basenames and source names intact", async () => {
    useUserStorageStore.setState({
      settings: {
        isRestricted: true,
        roots: [{ path: "/Volumes/nas-bakabase" }, { path: "/Volumes/nas-anobaka" }],
      } as NonNullable<ReturnType<typeof useUserStorageStore.getState>["settings"]>,
    });
    const root = new RootEntry();

    root.children = [
      new Entry({
        ...directory("/Volumes/nas-bakabase", "nas-bakabase"),
        type: IwFsType.Drive,
        parent: root,
        properties: [],
      }),
      new Entry({
        ...directory("/Volumes/nas-anobaka", "nas-anobaka"),
        type: IwFsType.Drive,
        parent: root,
        properties: [],
      }),
      new Entry({
        ...directory("/Volumes/nas-bakabase/Downloads", "Downloads"),
        parent: root,
        properties: [],
      }),
    ];
    mount(root);

    expect(await screen.findByText("/Volumes/nas-bakabase")).toBeInTheDocument();
    expect(screen.getByText("/Volumes/nas-anobaka")).toBeInTheDocument();
    expect(screen.getByText("Downloads")).toBeInTheDocument();
    expect(root.children[0].name).toBe("nas-bakabase");
    await act(async () => {});
  });

  it("preserves standalone Windows drive labels", async () => {
    useUserStorageStore.setState({
      settings: { isRestricted: false, roots: [{ path: "C:/" }] } as NonNullable<
        ReturnType<typeof useUserStorageStore.getState>["settings"]
      >,
    });
    const root = new RootEntry();

    root.children = [
      new Entry({
        path: "C:/",
        name: "Windows (C:)",
        type: IwFsType.Drive,
        parent: root,
        properties: [],
      }),
    ];
    mount(root);

    expect(await screen.findByText("Windows (C:)")).toBeInTheDocument();
    expect(screen.queryByText("C:/")).toBeNull();
    await act(async () => {});
  });

  it("shows loading instead of empty, and lists NAS folders without waiting for watcher setup", async () => {
    let resolveChildren!: (response: unknown) => void;
    let resolveWatcher!: (response: unknown) => void;

    mocks.children.mockReturnValue(
      new Promise((resolve) => {
        resolveChildren = resolve;
      }),
    );
    mocks.watch.mockReturnValue(
      new Promise((resolve) => {
        resolveWatcher = resolve;
      }),
    );
    mount(new RootEntry("/Volumes/nas-bakabase"));

    expect(screen.getByRole("status")).toHaveTextContent("fileExplorer.directory.loading");
    expect(screen.queryByText("fileExplorer.empty.noContent")).toBeNull();
    await waitFor(() => expect(mocks.watch).toHaveBeenCalled());
    expect(mocks.children).toHaveBeenCalled();
    await act(async () =>
      resolveChildren({
        data: { entries: [directory("/Volumes/nas-bakabase/Downloads", "Downloads")] },
      }),
    );
    expect(screen.getByText("Downloads")).toBeInTheDocument();
    expect(screen.getAllByTestId("directory-list")[0]).toHaveAttribute("data-width", "500");
    expect(mocks.initialized).toHaveBeenCalled();
    await act(async () => resolveWatcher({ code: 0 }));
  });

  it("shows a verified empty directory even while watcher setup is still pending", async () => {
    let resolveWatcher!: (response: unknown) => void;

    mocks.children.mockResolvedValue({ data: { entries: [] } });
    mocks.watch.mockReturnValue(
      new Promise((resolve) => {
        resolveWatcher = resolve;
      }),
    );
    mount(new RootEntry("/empty"));

    await waitFor(() =>
      expect(screen.getByText("fileExplorer.empty.noContent")).toBeInTheDocument(),
    );
    expect(screen.queryByRole("status")).toBeNull();
    await act(async () => resolveWatcher({ code: 0 }));
  });

  it("shows a directory-listing error and permits a successful retry", async () => {
    mocks.children
      .mockResolvedValueOnce({ code: 403, message: "Permission denied" })
      .mockResolvedValueOnce({
        data: { entries: [directory("/Volumes/nas-bakabase/Downloads", "Downloads")] },
      });
    mount(new RootEntry("/Volumes/nas-bakabase"));

    await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent("Permission denied"));
    expect(screen.queryByText("fileExplorer.empty.noContent")).toBeNull();
    await act(async () =>
      fireEvent.click(screen.getByRole("button", { name: "fileExplorer.storage.retry" })),
    );
    expect(screen.getByText("Downloads")).toBeInTheDocument();
    expect(screen.queryByRole("alert")).toBeNull();
  });

  it("ignores a previous directory response after the entry unmounts", async () => {
    let resolvePrevious!: (response: unknown) => void;

    mocks.children
      .mockReturnValueOnce(
        new Promise((resolve) => {
          resolvePrevious = resolve;
        }),
      )
      .mockResolvedValue({ data: { entries: [directory("/new/Downloads", "Downloads")] } });
    const oldRoot = new RootEntry("/previous");
    const { unmount } = mount(oldRoot);

    await waitFor(() => expect(mocks.children).toHaveBeenCalled());
    unmount();
    mocks.initialized.mockClear();
    mount(new RootEntry("/new"));
    await waitFor(() => expect(screen.getByText("Downloads")).toBeInTheDocument());
    const calls = mocks.initialized.mock.calls.length;

    await act(async () =>
      resolvePrevious({ data: { entries: [directory("/previous/Old", "Old")] } }),
    );
    expect(screen.queryByText("Old")).toBeNull();
    expect(oldRoot.children).toBeUndefined();
    expect(mocks.initialized).toHaveBeenCalledTimes(calls);
  });
});

describe("directory expansion dimensions", () => {
  it("grows every ancestor row for nested contents and reclaims the space after collapse", async () => {
    const listings = {
      "/downloads": [directory("/downloads/outer", "Outer folder")],
      "/downloads/outer": [
        directory("/downloads/outer/inner", "Inner folder"),
        { path: "/downloads/outer/summary.txt", name: "summary.txt", type: IwFsType.Unknown },
      ],
      "/downloads/outer/inner": ["one.txt", "two.txt", "three.txt"].map((name) => ({
        path: `/downloads/outer/inner/${name}`,
        name,
        type: IwFsType.Unknown,
      })),
    };
    mocks.children.mockImplementation(async ({ root }: { root: keyof typeof listings }) => ({
      data: { entries: listings[root] },
    }));
    const root = new RootEntry("/downloads", false, false);
    const contentHeight = vi.fn();
    render(<FileExplorerEntry entry={root} onChildrenLoaded={mocks.initialized} onContentHeightChange={contentHeight} />);
    await screen.findByText("Outer folder");
    const rootList = () => screen.getAllByTestId("directory-list")[0];
    const rowHeights = (list: HTMLElement) => JSON.parse(list.dataset.rowHeights!) as number[];
    const collapsedHeight = rowHeights(rootList())[0];
    expect(collapsedHeight).toBeGreaterThan(0);
    await waitFor(() => expect(contentHeight).toHaveBeenLastCalledWith(collapsedHeight));

    const outer = root.children![0];
    await act(async () => outer.expand());
    await screen.findByText("Inner folder");
    await waitFor(() => expect(rowHeights(rootList())).toEqual([collapsedHeight * 3]));
    await waitFor(() => expect(contentHeight).toHaveBeenLastCalledWith(collapsedHeight * 3));
    const inner = outer.children![0];
    await act(async () => inner.expand());
    await screen.findByText("three.txt");
    await waitFor(() => {
      const lists = screen.getAllByTestId("directory-list");
      expect(rowHeights(lists[0])).toEqual([collapsedHeight * 6]);
      expect(rowHeights(lists[1])).toEqual([collapsedHeight * 4, collapsedHeight]);
      expect(lists[1]).toHaveAttribute("data-height", String(collapsedHeight * 5));
      expect(lists[2]).toHaveAttribute("data-height", String(collapsedHeight * 3));
    });
    await waitFor(() => expect(contentHeight).toHaveBeenLastCalledWith(collapsedHeight * 6));

    await act(async () => inner.collapse());
    expect(screen.queryByText("three.txt")).toBeNull();
    expect(rowHeights(rootList())).toEqual([collapsedHeight * 3]);
    await act(async () => outer.collapse());
    expect(screen.queryByText("Inner folder")).toBeNull();
    expect(rowHeights(rootList())).toEqual([collapsedHeight]);
    await waitFor(() => expect(contentHeight).toHaveBeenLastCalledWith(collapsedHeight));
    expect(mocks.children).toHaveBeenCalledTimes(3);
  });

  it("refreshes the root viewport when its host changes height without reloading files", async () => {
    let viewportHeight = 180;
    vi.spyOn(HTMLElement.prototype, "clientHeight", "get").mockImplementation(() => viewportHeight);
    mocks.children.mockResolvedValue({
      data: { entries: [directory("/downloads/outer", "Outer folder")] },
    });
    const root = new RootEntry("/downloads", false, false);
    mount(root);
    await screen.findByText("Outer folder");
    const list = () => screen.getByTestId("directory-list");
    expect(list()).toHaveAttribute("data-height", "180");
    const originalRows = list().dataset.rowHeights;

    for (const nextHeight of [420, 120]) {
      viewportHeight = nextHeight;
      act(() => {
        for (const observer of mocks.resizeObservers) {
          if (observer.targets.has(root.dom!)) observer.notify();
        }
      });
      await waitFor(() => expect(list()).toHaveAttribute("data-height", String(nextHeight)));
      expect(list().dataset.rowHeights).toBe(originalRows);
    }
    expect(mocks.children).toHaveBeenCalledTimes(1);
  });
});

describe("file explorer drag/drop capabilities", () => {
  const makeRoot = () => {
    const root = new RootEntry();
    root.children = [
      new Entry({
        ...directory("/downloads/target", "Target folder"),
        parent: root,
        properties: [],
      }),
    ];
    return root;
  };
  const transfer = (paths: unknown = ["/downloads/source"]) => ({
    getData: vi.fn(() => JSON.stringify(paths)),
    setData: vi.fn(),
    effectAllowed: "",
    dropEffect: "",
  });

  it.each([undefined, ["enter-directory"] as const])(
    "blocks drag and external drops without move permission (%j)",
    async (capabilities) => {
      render(
        <FileExplorerEntry
          entry={makeRoot()}
          capabilities={capabilities ? [...capabilities] : undefined}
        />,
      );
      const row = (await screen.findByText("Target folder")).closest("[draggable]")!;
      const dataTransfer = transfer();
      expect(row).toHaveAttribute("draggable", "false");
      fireEvent.dragStart(row, { dataTransfer });
      fireEvent.dragOver(row, { dataTransfer });
      fireEvent.drop(row, { dataTransfer });
      expect(dataTransfer.setData).not.toHaveBeenCalled();
      expect(dataTransfer.dropEffect).toBe("none");
      expect(mocks.move).not.toHaveBeenCalled();
    },
  );

  it("retains explicit move support and rejects malformed external payloads", async () => {
    render(<FileExplorerEntry entry={makeRoot()} capabilities={["move"]} />);
    const row = (await screen.findByText("Target folder")).closest("[draggable]")!;
    expect(row).toHaveAttribute("draggable", "true");
    fireEvent.drop(row, { dataTransfer: transfer({ path: "/downloads/source" }) });
    fireEvent.drop(row, { dataTransfer: transfer([123]) });
    expect(mocks.move).not.toHaveBeenCalled();
    const dataTransfer = transfer();
    fireEvent.dragStart(row, { dataTransfer });
    expect(dataTransfer.setData).toHaveBeenCalledWith(
      "text/plain",
      JSON.stringify(["/downloads/target"]),
    );
    await act(async () => fireEvent.drop(row, { dataTransfer }));
    expect(mocks.move).toHaveBeenCalledExactlyOnceWith({
      destDir: "/downloads/target",
      entryPaths: ["/downloads/source"],
    });
  });
});


it("expands a registered directory using the common virtualized rows and only its manifest source", async () => {
  const loadChildren = vi.fn().mockResolvedValue({ childCount: 1,
    entries: [{ relativePath: "Folder/picture.jpg", isDirectory: false, length: 300 }] });
  const onNavigate = vi.fn();
  const page = { directory: "", entries: [{ relativePath: "Folder", isDirectory: true }], onNavigate, loadChildren };
  const root = new RootEntry("", false, false, page);
  root.children = pageChildren(root, page.entries);
  root.expireFilteredChildren();
  mount(root);
  await screen.findByText("Folder");
  await act(async () => { root.children![0].expand(); });
  expect(await screen.findByText("picture.jpg")).toBeVisible();
  expect(loadChildren).toHaveBeenCalledWith("Folder", expect.any(AbortSignal));
  expect(mocks.children).not.toHaveBeenCalled();
  expect(mocks.watch).not.toHaveBeenCalled();
  loadChildren.mockResolvedValue({ childCount: 60,
    entries: [{ relativePath: "Folder/sample.jpg", isDirectory: false, length: 1 }] });
  await act(async () => { root.children![0].expand(true); });
  expect(onNavigate).toHaveBeenCalledWith("Folder");
});
