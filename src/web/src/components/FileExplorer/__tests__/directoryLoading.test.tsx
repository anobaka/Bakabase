import type { ReactNode } from "react";

import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import FileExplorerEntry from "../FileExplorerEntry";

import RootEntry from "@/core/models/FileExplorer/RootEntry";
import { IwFsType } from "@/sdk/constants";

const mocks = vi.hoisted(() => ({
  children: vi.fn(),
  watch: vi.fn(),
  stop: vi.fn(),
  initialized: vi.fn(),
  uuid: 0,
}));

vi.mock("@/sdk/BApi", () => ({
  default: {
    file: {
      getChildrenIwFsInfo: mocks.children,
      startWatchingChangesInFileProcessorWorkspace: mocks.watch,
      stopWatchingChangesInFileProcessorWorkspace: mocks.stop,
      keepAliveFileProcessorWatcher: vi.fn(),
      getIwFsInfo: vi.fn().mockResolvedValue({ data: { childrenCount: 0 } }),
    },
  },
}));
vi.mock("@/components/utils", () => ({
  buildLogger: () => () => {},
  standardizePath: (path: string) => path,
  getStandardParentPath: (path: string) => path?.slice(0, path.lastIndexOf("/")),
  humanFileSize: () => "",
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
    default: forwardRef<HTMLSpanElement, { name: string }>(({ name }, ref) => (
      <span ref={ref}>{name}</span>
    )),
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
  toast: { danger: vi.fn() },
}));
vi.mock("react-virtualized", async () => {
  const { forwardRef, useImperativeHandle } = await import("react");

  return {
    List: forwardRef(
      (
        {
          rowCount,
          rowRenderer,
          width,
        }: { width?: number; rowCount: number; rowRenderer: (props: object) => ReactNode },
        ref,
      ) => {
        useImperativeHandle(ref, () => ({ recomputeRowHeights: vi.fn(), scrollToRow: vi.fn() }));

        return (
          <div data-testid="directory-list" data-width={width}>
            {Array.from({ length: rowCount }, (_, index) =>
              rowRenderer({ index, style: { height: 36, width: 500 } }),
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
  vi.stubGlobal(
    "ResizeObserver",
    class {
      observe = vi.fn();
      disconnect = vi.fn();
    },
  );
  mocks.stop.mockResolvedValue({ code: 0 });
  mocks.watch.mockResolvedValue({ code: 0 });
  vi.spyOn(HTMLElement.prototype, "clientHeight", "get").mockReturnValue(300);
  vi.spyOn(HTMLElement.prototype, "clientWidth", "get").mockReturnValue(500);
});
afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe("directory listing initialization", () => {
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
