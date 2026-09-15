import { act } from "react-dom/test-utils";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { UIHubConnection } from "../UIHubConnection";

import { useDownloadTasksStore } from "@/stores/downloadTasks";

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
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
  vi.clearAllTimers();
  vi.useRealTimers();
});

describe("download task snapshot lifetime", () => {
  it("invalidates old snapshots on connecting, disconnecting and unmounting, then accepts a new full snapshot", async () => {
    await act(async () => {
      root.render(<UIHubConnection />);
    });
    expect(connection.send).toHaveBeenCalledWith("GetInitialData");
    expect(useDownloadTasksStore.getState().initialized).toBe(false);

    act(() => handlers.get("GetData")!("DownloadTask", []));
    expect(useDownloadTasksStore.getState().initialized).toBe(true);

    await act(async () => {
      await closed.callback!();
    });
    expect(useDownloadTasksStore.getState().initialized).toBe(false);
    act(() => handlers.get("GetIncrementalData")!("DownloadTask", { id: 1 }));
    await act(async () => {
      await vi.advanceTimersByTimeAsync(160);
    });
    expect(useDownloadTasksStore.getState().initialized).toBe(false);

    act(() => handlers.get("GetData")!("DownloadTask", []));
    expect(useDownloadTasksStore.getState().initialized).toBe(true);
    act(() => root.render(<></>));
    expect(useDownloadTasksStore.getState().initialized).toBe(false);
  });
});
