import type { ReactNode } from "react";
import type { DownloadTask } from "@/core/models/DownloadTask";
import type { DownloadTaskFilter } from "../components/DownloadTaskFilters";

import { useEffect } from "react";
import { createRoot, type Root } from "react-dom/client";
import { act } from "react-dom/test-utils";
import { renderToStaticMarkup } from "react-dom/server";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import DownloaderPage from "..";

import { useDownloadTasksStore } from "@/stores/downloadTasks";
import {
  DownloadTaskActionOnConflict,
  DownloadTaskStatus,
  ResponseCode,
  ThirdPartyId,
} from "@/sdk/constants";

const { getDefinitions, startTasks, stopTasks, createPortal, directDownload, directResults } =
  vi.hoisted(() => ({
    getDefinitions: vi.fn(),
    startTasks: vi.fn(),
    stopTasks: vi.fn(),
    createPortal: vi.fn(),
    directDownload: vi.fn(),
    directResults: new Map<number, Promise<void>>(),
  }));

vi.mock("../directDownload", () => ({ downloadTaskDirectly: directDownload }));
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
  useBakabaseContext: () => ({ createPortal }),
}));
vi.mock("@/config/env.ts", () => ({ toAbsoluteBackendUrl: (url: string) => url }));
// Workflow help has its own configuration dependencies, outside task selection and filtering.
vi.mock("@/components/Workflow/WorkflowIntegrationHint", () => ({ default: () => null }));
vi.mock("../components/Configurations", () => ({ default: () => null }));
vi.mock("../components/TaskDetailModal", () => ({ default: () => null }));
vi.mock("../components/BatchEditModal", () => ({ default: () => null }));
vi.mock("../components/RequestStatistics", () => ({ default: () => null }));
vi.mock("../components/TaskRow", () => ({
  DOWNLOAD_TASK_ITEM_HEIGHT: 100,
  default: ({
    task,
    onClick,
    onShowError,
    onDownloadDirectly,
  }: {
    task: DownloadTask;
    onClick: (id: number, event: unknown) => void;
    onShowError: (task: DownloadTask) => void;
    onDownloadDirectly: (id: number) => Promise<void>;
  }) => (
    <>
      <button data-row-id={task.id} onClick={(event) => onClick(task.id, event)}>
        {task.name}
      </button>
      <button onClick={() => onShowError(task)}>{`message-${task.id}`}</button>
      <button
        onClick={() => {
          const result = onDownloadDirectly(task.id);

          directResults.set(task.id, result);
          // The real row reports this rejection as a visible error; tests inspect it below.
          void result.catch(() => undefined);
        }}
      >{`direct-${task.id}`}</button>
    </>
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
  directResults.clear();
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
  directDownload.mockResolvedValue({ code: ResponseCode.Success });
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
  it.each([
    ["MacIntel", "metaKey", "ctrlKey"],
    ["Win32", "ctrlKey", "metaKey"],
    ["Linux x86_64", "ctrlKey", "metaKey"],
  ])("uses %s select-all without taking it from an editor", async (platform, primary, other) => {
    vi.stubGlobal("navigator", { platform });
    await act(async () => root.render(<DownloaderPage />));
    button("Alpha")!.focus();
    await act(async () => {
      button("Alpha")!.dispatchEvent(
        new KeyboardEvent("keydown", { key: "a", [other]: true, bubbles: true }),
      );
    });
    expect(button("downloader.action.stopSelected")).toBeUndefined();
    await act(async () => {
      button("Alpha")!.dispatchEvent(
        new KeyboardEvent("keydown", { key: "a", [primary]: true, bubbles: true }),
      );
    });
    await choose("downloader.action.stopSelected");
    expect(stopTasks).toHaveBeenLastCalledWith([11, 22, 33]);

    await choose("Alpha");
    const input = document.createElement("input");

    button("Alpha")!.parentElement!.appendChild(input);
    input.focus();
    await act(async () => {
      input.dispatchEvent(
        new KeyboardEvent("keydown", { key: "a", [primary]: true, bubbles: true }),
      );
    });
    await choose("downloader.action.stopSelected");
    expect(stopTasks).toHaveBeenLastCalledWith([11]);
  });

  it("uses Command-click for Mac multi-selection and leaves Control-click distinct", async () => {
    vi.stubGlobal("navigator", { platform: "MacIntel" });
    await act(async () => root.render(<DownloaderPage />));
    await choose("Alpha");
    await act(async () => {
      button("Bravo")!.dispatchEvent(new MouseEvent("click", { metaKey: true, bubbles: true }));
    });
    await choose("downloader.action.stopSelected");
    expect(stopTasks).toHaveBeenLastCalledWith([11, 22]);
    await act(async () => {
      button("Charlie")!.dispatchEvent(new MouseEvent("click", { ctrlKey: true, bubbles: true }));
    });
    await choose("downloader.action.stopSelected");
    expect(stopTasks).toHaveBeenLastCalledWith([33]);
  });

  it("starts a direct download with one atomic task action and no extra normal start", async () => {
    await act(async () => root.render(<DownloaderPage />));
    await choose("direct-22");
    await directResults.get(22);

    expect(directDownload).toHaveBeenCalledExactlyOnceWith(22);
    expect(startTasks).not.toHaveBeenCalled();
    expect(stopTasks).not.toHaveBeenCalled();
  });

  it.each([
    ["first", DownloadTaskActionOnConflict.StopOthers],
    ["queue", DownloadTaskActionOnConflict.Ignore],
  ])(
    "retries direct download after choosing %s in the download order prompt",
    async (choice, action) => {
      directDownload
        .mockResolvedValueOnce({ code: ResponseCode.Conflict, message: "FailedToStart" })
        .mockResolvedValueOnce({ code: ResponseCode.Success });
      await act(async () => root.render(<DownloaderPage />));
      await choose("direct-22");
      const modal = createPortal.mock.calls.at(-1)![1];

      expect(modal.title).toBe("downloader.downloadOrder.title");
      const body = renderToStaticMarkup(modal.children);

      expect(body).toContain("downloader.downloadOrder.queueHint");
      expect(body).toContain("downloader.downloadOrder.priorityHint");
      expect(body).not.toContain("FailedToStart");
      expect(modal.footer.cancelProps).toMatchObject({
        children: "downloader.action.addToQueue",
        color: "primary",
        autoFocus: true,
      });
      expect(modal.footer.okProps).toMatchObject({
        children: "downloader.action.downloadSelectedFirst",
        color: "default",
      });
      expect(directDownload).toHaveBeenCalledTimes(1);

      await act(async () => {
        if (choice === "first") modal.onOk();
        // Real Modal closes after OK too; that must not choose Ignore a second time.
        modal.onClose();
        await directResults.get(22);
      });

      expect(directDownload.mock.calls).toEqual([[22], [22, action]]);
      expect(startTasks).not.toHaveBeenCalled();
    },
  );

  it.each([
    ["first", DownloadTaskActionOnConflict.StopOthers],
    ["queue", DownloadTaskActionOnConflict.Ignore],
  ])("starts the selected tasks only once after choosing %s", async (choice, action) => {
    startTasks
      .mockResolvedValueOnce({ code: ResponseCode.Conflict, message: "Another task is running" })
      .mockResolvedValueOnce({ code: ResponseCode.Success });
    await act(async () => root.render(<DownloaderPage />));
    await choose("Bravo");
    await choose("downloader.action.startSelected");
    const modal = createPortal.mock.calls.at(-1)![1];

    expect(modal.title).toBe("downloader.downloadOrder.title");
    expect(renderToStaticMarkup(modal.children)).toContain("Another task is running");
    expect(useDownloadTasksStore.getState().tasks.find((item) => item.id === 22)!.status).toBe(
      DownloadTaskStatus.Idle,
    );

    await act(async () => {
      if (choice === "first") await modal.onOk();
      // Closing follows OK as well as the queue button, Escape and the close button.
      await modal.onClose();
    });

    expect(startTasks.mock.calls.map(([payload]) => payload)).toEqual([
      { ids: [22], actionOnConflict: DownloadTaskActionOnConflict.NotSet },
      { ids: [22], actionOnConflict: action },
    ]);
  });

  it("can enqueue after a rejected priority request leaves the prompt open", async () => {
    startTasks
      .mockResolvedValueOnce({ code: ResponseCode.Conflict, message: "Another task is running" })
      .mockRejectedValueOnce(new Error("Network unavailable"))
      .mockResolvedValueOnce({ code: ResponseCode.Success });
    await act(async () => root.render(<DownloaderPage />));
    await choose("Bravo");
    await choose("downloader.action.startSelected");
    const modal = createPortal.mock.calls.at(-1)![1];

    await expect(modal.onOk()).rejects.toThrow("Network unavailable");
    await modal.onClose();

    expect(startTasks.mock.calls.map(([payload]) => payload)).toEqual([
      { ids: [22], actionOnConflict: DownloadTaskActionOnConflict.NotSet },
      { ids: [22], actionOnConflict: DownloadTaskActionOnConflict.StopOthers },
      { ids: [22], actionOnConflict: DownloadTaskActionOnConflict.Ignore },
    ]);
  });

  it("propagates a refused direct download so the row can display the failure", async () => {
    directDownload.mockResolvedValueOnce({
      code: ResponseCode.InvalidPayloadOrOperation,
      message: "Cookie expired",
    });
    await act(async () => root.render(<DownloaderPage />));
    await choose("direct-22");

    await expect(directResults.get(22)).rejects.toThrow("Cookie expired");
    expect(createPortal).not.toHaveBeenCalled();
    expect(startTasks).not.toHaveBeenCalled();
    expect(useDownloadTasksStore.getState().tasks.find((item) => item.id === 22)!.status).toBe(
      DownloadTaskStatus.Idle,
    );
  });

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

  it.each([
    [DownloadTaskStatus.Failed, "common.label.error", undefined],
    [DownloadTaskStatus.Complete, "downloader.label.notices", "downloader.tip.clickToCopyNotices"],
  ] as const)(
    "opens the message of a task in status %s under the matching title",
    async (status, title, copyTip) => {
      useDownloadTasksStore
        .getState()
        .setTasks([
          { ...task(11, "Alpha", ThirdPartyId.Bilibili), status, message: "line 1\nline 2" },
        ]);
      await act(async () => root.render(<DownloaderPage />));
      await choose("message-11");

      expect(createPortal).toHaveBeenCalledOnce();
      const [, props] = createPortal.mock.calls[0];

      expect(props.title).toBe(title);
      expect(props.children.props).toEqual({ copyTip, message: "line 1\nline 2" });
    },
  );
});
