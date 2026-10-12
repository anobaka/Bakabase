import { act } from "react-dom/test-utils";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { UIHubConnection } from "../UIHubConnection";

import { useDownloadTasksStore } from "@/stores/downloadTasks";
import { usePathMarksStore } from "@/stores/pathMarks";
import { useBTasksStore } from "@/stores/bTasks";

const { handlers, connection, closed } = vi.hoisted(() => {
  const handlers = new Map<string, (...args: any[]) => void>();
  const closed = { callback: undefined as undefined | (() => Promise<void>) };
  const connection = {
    state: "Disconnected",
    on: vi.fn((name: string, handler: (...args: any[]) => void) => handlers.set(name, handler)),
    onclose: vi.fn((callback: () => Promise<void>) => {
      closed.callback = callback;
    }),
    start: vi.fn(async () => {
      connection.state = "Connected";
    }),
    stop: vi.fn(async () => {
      connection.state = "Disconnected";
    }),
    send: vi.fn(async () => undefined),
  };

  return { handlers, connection, closed };
});

vi.mock("@microsoft/signalr", () => ({
  HubConnectionState: { Disconnected: "Disconnected", Connected: "Connected" },
  LogLevel: { Information: "Information" },
  HubConnectionBuilder: class {
    withUrl() {
      return this;
    }
    configureLogging() {
      return this;
    }
    build() {
      return connection;
    }
  },
}));
vi.mock("@/components/bakaui", () => ({ toast: {} }));
vi.mock("@/components/utils", () => ({ buildLogger: () => () => undefined }));
vi.mock("@/stores/options", () => ({ optionsStores: {} }));

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  vi.useFakeTimers();
  handlers.clear();
  connection.state = "Disconnected";
  connection.send.mockClear();
  (globalThis as any).IS_REACT_ACT_ENVIRONMENT = true;
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
  useDownloadTasksStore.getState().setTasks([]);
  usePathMarksStore.setState({ marks: new Map(), deletedMarkIds: new Set() });
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
  vi.clearAllTimers();
  vi.useRealTimers();
});

describe("download task snapshot lifetime", () => {
  it("removes a deleted path mark and ignores late updates until an authoritative snapshot introduces it again", async () => {
    await act(async () => root.render(<UIHubConnection />));
    const mark = { id: 7, path: "/data/library", syncStatus: 1 } as any;

    act(() => handlers.get("GetIncrementalData")!("PathMark", mark));
    expect(usePathMarksStore.getState().marks.get(7)).toEqual(mark);
    act(() => handlers.get("DeleteData")!("Other", 7));
    expect(usePathMarksStore.getState().marks.has(7)).toBe(true);
    act(() => handlers.get("DeleteData")!("PathMark", 7));
    expect(usePathMarksStore.getState().marks.has(7)).toBe(false);
    expect(usePathMarksStore.getState().deletedMarkIds.has(7)).toBe(true);
    act(() => handlers.get("GetIncrementalData")!("PathMark", { ...mark, syncStatus: 3 }));
    expect(usePathMarksStore.getState().marks.has(7)).toBe(false);
    expect(usePathMarksStore.getState().deletedMarkIds.has(7)).toBe(true);
    usePathMarksStore.getState().setMarks([{ ...mark, syncStatus: 3 }]);
    expect(usePathMarksStore.getState().marks.get(7)?.syncStatus).toBe(3);
    expect(usePathMarksStore.getState().deletedMarkIds.has(7)).toBe(false);
  });

  it("invalidates old snapshots on connecting, disconnecting and unmounting, then accepts a new full snapshot", async () => {
    await act(async () => {
      root.render(<UIHubConnection />);
    });
    expect(connection.send).toHaveBeenCalledWith("GetInitialData");
    expect(useBTasksStore.getState().initialized).toBe(false);
    act(() => handlers.get("GetIncrementalData")!("BTask", { id: "SyncResources", status: 1 }));
    expect(useBTasksStore.getState().initialized).toBe(false);
    act(() => handlers.get("GetData")!("BTask", []));
    expect(useBTasksStore.getState().initialized).toBe(true);
    expect(useDownloadTasksStore.getState().initialized).toBe(false);

    act(() => handlers.get("GetData")!("DownloadTask", []));
    expect(useDownloadTasksStore.getState().initialized).toBe(true);

    await act(async () => {
      await closed.callback!();
    });
    expect(useDownloadTasksStore.getState().initialized).toBe(false);
    expect(useBTasksStore.getState().initialized).toBe(false);
    act(() => handlers.get("GetIncrementalData")!("DownloadTask", { id: 1 }));
    await act(async () => {
      await vi.advanceTimersByTimeAsync(160);
    });
    expect(useDownloadTasksStore.getState().initialized).toBe(false);

    act(() => handlers.get("GetData")!("DownloadTask", []));
    expect(useDownloadTasksStore.getState().initialized).toBe(true);
    act(() => handlers.get("GetData")!("BTask", []));
    expect(useBTasksStore.getState().initialized).toBe(true);
    act(() => root.render(<></>));
    expect(useDownloadTasksStore.getState().initialized).toBe(false);
    expect(useBTasksStore.getState().initialized).toBe(false);
  });
});
