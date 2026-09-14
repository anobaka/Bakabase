import type { ReactNode } from "react";
import type { DownloadTask } from "@/core/models/DownloadTask";
import type { DownloadTaskFilter } from "../components/DownloadTaskFilters";

import { useEffect } from "react";
import { createRoot, type Root } from "react-dom/client";
import { act } from "react-dom/test-utils";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import DownloaderPage from "..";

import { useDownloadTasksStore } from "@/stores/downloadTasks";
import {
  DownloadTaskActionOnConflict,
  DownloadTaskStatus,
  ResponseCode,
  ThirdPartyId,
} from "@/sdk/constants";

const { getDefinitions, startTasks, stopTasks } = vi.hoisted(() => ({
  getDefinitions: vi.fn(),
  startTasks: vi.fn(),
  stopTasks: vi.fn(),
}));

vi.mock("@/sdk/BApi", () => ({
  default: {
    downloadTask: {
      getAllDownloaderDefinitions: getDefinitions,
      startDownloadTasks: startTasks,
      stopDownloadTasks: stopTasks,
    },
  },
}));
vi.mock("react-use", () => ({ useUpdate: () => () => undefined, useUpdateEffect: useEffect }));
vi.mock("@szhsin/react-menu", () => ({
  ControlledMenu: () => null,
  MenuItem: () => null,
  useMenuState: () => [{}, () => undefined],
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: vi.fn() }),
}));
vi.mock("@/config/env.ts", () => ({ toAbsoluteBackendUrl: (url: string) => url }));
vi.mock("../components/Configurations", () => ({ default: () => null }));
vi.mock("../components/TaskDetailModal", () => ({ default: () => null }));
vi.mock("../components/BatchEditModal", () => ({ default: () => null }));
vi.mock("../components/RequestStatistics", () => ({ default: () => null }));
vi.mock("../components/TaskRow", () => ({
  DOWNLOAD_TASK_ITEM_HEIGHT: 100,
  default: ({
    task,
    onClick,
  }: {
    task: DownloadTask;
    onClick: (id: number, event: unknown) => void;
  }) => (
    <button data-row-id={task.id} onClick={(event) => onClick(task.id, event)}>
      {task.name}
    </button>
  ),
}));
vi.mock("../components/DownloadTaskFilters", () => ({
  default: ({
    value,
    onChange,
    sources,
  }: {
    value: DownloadTaskFilter;
    onChange: (value: DownloadTaskFilter) => void;
    sources: { value: ThirdPartyId; label: string }[];
  }) => (
    <div>
      <input
        aria-label="Task keyword"
        value={value.keyword ?? ""}
        onChange={(event) => onChange({ ...value, keyword: event.target.value })}
      />
      <select
        aria-label="Task source"
        value={value.thirdPartyId ?? "all"}
        onChange={(event) =>
          onChange({
            ...value,
            thirdPartyId:
              event.target.value === "all"
                ? undefined
                : (Number(event.target.value) as ThirdPartyId),
          })
        }
      >
        <option value="all">All sources</option>
        {sources.map((source) => (
          <option key={source.value} value={source.value}>
            {source.label}
          </option>
        ))}
      </select>
    </div>
  ),
}));
vi.mock("@/components/bakaui", () => ({
  Button: ({
    children,
    onPress,
    isDisabled,
    "aria-label": label,
  }: {
    children: ReactNode;
    onPress: () => void;
    isDisabled?: boolean;
    "aria-label"?: string;
  }) => (
    <button aria-label={label} disabled={isDisabled} onClick={onPress}>
      {children}
    </button>
  ),
  Listbox: ({ children }: { children: ReactNode }) => <div role="listbox">{children}</div>,
  ListboxItem: ({ children }: { children: ReactNode }) => (
    <div aria-selected={false} role="option">
      {children}
    </div>
  ),
  Tooltip: ({ children }: { children: ReactNode }) => <>{children}</>,
  Dropdown: () => null,
  DropdownItem: () => null,
  DropdownMenu: () => null,
  DropdownTrigger: () => null,
  Modal: () => null,
  toast: { success: vi.fn(), warning: vi.fn() },
}));

const task = (id: number, name: string, thirdPartyId: ThirdPartyId): DownloadTask => ({
  id,
  name,
  key: `gallery-${id}`,
  thirdPartyId,
  type: 1,
  progress: 0,
  downloadStatusUpdateDt: new Date("2026-09-14T00:00:00Z"),
  status: DownloadTaskStatus.Idle,
  failureTimes: 0,
  autoRetry: false,
  availableActions: [],
  displayName: name,
  canStart: true,
  createdAt: "2026-09-14T00:00:00Z",
});
let container: HTMLDivElement;
let root: Root;
const button = (name: string) =>
  Array.from(container.querySelectorAll("button")).find((item) => item.textContent === name);
const choose = async (name: string) => {
  await act(async () => button(name)!.click());
};
const source = async (value: ThirdPartyId | "all") => {
  await act(async () => {
    const select = container.querySelector("select")!;

    select.value = String(value);
    select.dispatchEvent(new Event("change", { bubbles: true }));
  });
};
const rowIds = () =>
  Array.from(container.querySelectorAll("[data-row-id]")).map((row) =>
    Number(row.getAttribute("data-row-id")),
  );

beforeEach(() => {
  vi.clearAllMocks();
  vi.stubGlobal("IS_REACT_ACT_ENVIRONMENT", true);
  vi.stubGlobal(
    "ResizeObserver",
    class {
      observe() {}
      unobserve() {}
      disconnect() {}
    },
  );
  vi.spyOn(HTMLElement.prototype, "clientHeight", "get").mockReturnValue(600);
  getDefinitions.mockResolvedValue({
    data: [{ thirdPartyId: ThirdPartyId.ExHentai }, { thirdPartyId: ThirdPartyId.Steam }],
  });
  startTasks.mockResolvedValue({ code: ResponseCode.Success });
  stopTasks.mockResolvedValue({ code: ResponseCode.Success });
  useDownloadTasksStore
    .getState()
    .setTasks([
      task(11, "Alpha", ThirdPartyId.ExHentai),
      task(22, "Bravo", ThirdPartyId.ExHentai),
      task(33, "Charlie", ThirdPartyId.Steam),
    ]);
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});
afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  useDownloadTasksStore.getState().setTasks([]);
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe("downloader page task selection", () => {
  it("starts and stops only the selected task from the toolbar", async () => {
    await act(async () => root.render(<DownloaderPage />));
    await choose("Bravo");
    await choose("downloader.action.startSelected");
    expect(startTasks).toHaveBeenCalledWith(
      { ids: [22], actionOnConflict: DownloadTaskActionOnConflict.NotSet },
      expect.objectContaining({ showErrorToast: expect.any(Function) }),
    );
    await choose("downloader.action.stopSelected");
    expect(stopTasks).toHaveBeenCalledWith([22]);
    const statuses = useDownloadTasksStore.getState().tasks.map((item) => [item.id, item.status]);

    expect(statuses).toEqual([
      [11, DownloadTaskStatus.Idle],
      [22, DownloadTaskStatus.Stopping],
      [33, DownloadTaskStatus.Idle],
    ]);
  });

  it("clears selection when filters change so hidden tasks cannot remain selected", async () => {
    await act(async () => root.render(<DownloaderPage />));
    await choose("Alpha");
    await source(ThirdPartyId.Steam);
    expect(rowIds()).toEqual([33]);
    expect(button("downloader.action.startSelected")).toBeUndefined();
    await source("all");
    expect(rowIds()).toEqual([11, 22, 33]);
    expect(button("downloader.action.startSelected")).toBeUndefined();
    await choose("Charlie");
    await choose("downloader.action.startSelected");
    expect(startTasks).toHaveBeenCalledWith(
      { ids: [33], actionOnConflict: DownloadTaskActionOnConflict.NotSet },
      expect.any(Object),
    );
    expect(startTasks).toHaveBeenCalledTimes(1);
  });

  it("resets an empty filter result and restores the complete list", async () => {
    await act(async () => root.render(<DownloaderPage />));
    await act(async () => {
      const input = container.querySelector("input")!;

      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(
        input,
        "no-matching-gallery",
      );
      input.dispatchEvent(new Event("input", { bubbles: true }));
    });
    expect(rowIds()).toEqual([]);
    expect(container).toHaveTextContent("downloader.empty.filteredTitle");
    await choose("downloader.filter.reset");
    expect(container.querySelector("input")).toHaveValue("");
    expect(rowIds()).toEqual([11, 22, 33]);
    expect(container).not.toHaveTextContent("downloader.empty.filteredTitle");
  });
});
