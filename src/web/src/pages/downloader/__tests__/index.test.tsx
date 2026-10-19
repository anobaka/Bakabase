import type { ReactNode } from "react";
import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import DownloaderPage from "..";
import { useDownloadTasksStore } from "@/stores/downloadTasks";
import {
  DownloadTaskActionOnConflict,
  DownloadTaskStatus,
  ResponseCode,
  ThirdPartyId,
} from "@/sdk/constants";
import type { CenterTask } from "../downloadCenter";
const mocks = vi.hoisted(() => ({
  get: vi.fn(),
  batch: vi.fn(),
  portal: vi.fn(),
  direct: vi.fn(),
  directResult: undefined as Promise<void> | undefined,
}));
vi.mock("@/sdk/BApi", () => ({
  default: {
    downloadCenter: { getDownloadCenterTasks: mocks.get, batchDownloadCenterTasks: mocks.batch },
    downloadTask: {},
    request: vi.fn(),
  },
}));
vi.mock("../directDownload", () => ({ downloadTaskDirectly: mocks.direct }));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: mocks.portal }),
}));
vi.mock("@/components/Workflow/WorkflowIntegrationHint", () => ({ default: () => null }));
vi.mock("@/config/env", () => ({ toAbsoluteBackendUrl: (value: string) => value }));
vi.mock("../components/Configurations", () => ({ default: () => null }));
vi.mock("../components/TaskDetailModal", () => ({ default: () => null }));
vi.mock("../components/BatchEditModal", () => ({ default: () => null }));
vi.mock("../components/AddGeneralDownloadModal", () => ({ default: () => null }));
vi.mock("../components/GeneralTaskDetailModal", () => ({ default: () => null }));
vi.mock("../components/RequestStatistics", () => ({
  default: () => <span>request-statistics</span>,
}));
vi.mock("@szhsin/react-menu", () => ({
  ControlledMenu: () => null,
  MenuItem: () => null,
  useMenuState: () => [{}, () => undefined],
}));
vi.mock("../components/TaskRow", () => ({
  DOWNLOAD_TASK_ITEM_HEIGHT: 128,
  DOWNLOAD_TASK_ROW_HEIGHT: 116,
  default: ({
    task,
    onClick,
    onToggleChildren,
    childCount,
    onShowError,
    onDownloadDirectly,
  }: any) => (
    <div>
      <button data-row-id={`site:${task.id}`} onClick={(event) => onClick(task.id, event)}>
        {task.name}
      </button>
      {childCount > 0 && (
        <button onClick={() => onToggleChildren(task.id)}>expand-{task.id}</button>
      )}
      <button onClick={() => onShowError(task)}>message-{task.id}</button>
      <button
        onClick={() => {
          mocks.directResult = onDownloadDirectly(task.id);
          void mocks.directResult!.catch(() => {});
        }}
      >
        direct-{task.id}
      </button>
    </div>
  ),
}));
vi.mock("../components/GeneralTaskRow", () => ({
  default: ({ task, onSelect, onAction, onSource }: any) => (
    <div>
      <button data-row-id={task.id} onClick={(event) => onSelect(task.id, event)}>
        {task.name}
      </button>
      {task.availableActions.map((action: string) => (
        <button key={action} onClick={() => onAction(task.id, action)}>
          {action}-{task.id}
        </button>
      ))}
      {task.siteTaskId != null && (
        <button onClick={() => onSource(task.siteTaskId)}>source-{task.id}</button>
      )}
    </div>
  ),
}));
vi.mock("@/components/bakaui", () => ({
  Button: ({
    children,
    onPress,
    isDisabled,
    "aria-label": label,
    "aria-pressed": pressed,
  }: any) => (
    <button aria-label={label} aria-pressed={pressed} disabled={isDisabled} onClick={onPress}>
      {children}
    </button>
  ),
  Input: ({ value, onValueChange, "aria-label": label }: any) => (
    <input
      aria-label={label}
      value={value}
      onChange={(event) => onValueChange(event.target.value)}
    />
  ),
  Select: ({ selectedKeys, dataSource, onSelectionChange, "aria-label": label }: any) => (
    <select
      aria-label={label}
      value={selectedKeys[0]}
      onChange={(event) => onSelectionChange(new Set([event.target.value]))}
    >
      {dataSource.map((item: any) => (
        <option key={item.value} value={item.value}>
          {item.label}
        </option>
      ))}
    </select>
  ),
  Listbox: ({ children, virtualization }: any) => (
    <div role="listbox" data-item-height={virtualization.itemHeight}>
      {children}
    </div>
  ),
  ListboxItem: ({ children }: { children: ReactNode }) => <div role="option">{children}</div>,
  Spinner: () => <span>loading</span>,
  Tooltip: ({ children }: { children: ReactNode }) => <>{children}</>,
  Dropdown: () => null,
  DropdownMenu: () => null,
  DropdownItem: () => null,
  DropdownTrigger: () => null,
  Modal: () => null,
  toast: { success: vi.fn(), warning: vi.fn(), danger: vi.fn() },
}));
const center = (id: string, overrides: Partial<CenterTask> = {}): CenterTask => ({
  id,
  providerTaskId: id,
  category: id.startsWith("site:") ? "site" : "general",
  provider: id.split(":")[0],
  outputDirectory: "/downloads",
  name: id,
  createdAt: "2026-10-19",
  updatedAt: "2026-10-19",
  originKind: "manual",
  files: [],
  status: "stopped",
  availableActions: ["start", "stop", "retry", "delete"],
  ...overrides,
});
let snapshot: CenterTask[];
const mount = (path = "/downloader") =>
  render(
    <MemoryRouter initialEntries={[path]}>
      <DownloaderPage />
    </MemoryRouter>,
  );
const rows = () =>
  Array.from(document.querySelectorAll("[data-row-id]")).map((item) =>
    item.getAttribute("data-row-id"),
  );
beforeEach(() => {
  vi.clearAllMocks();
  vi.stubGlobal(
    "ResizeObserver",
    class {
      observe() {}
      disconnect() {}
    },
  );
  vi.spyOn(HTMLElement.prototype, "clientHeight", "get").mockReturnValue(640);
  snapshot = [
    center("site:1", { name: "Gallery", siteTaskId: 1, thirdPartyId: ThirdPartyId.ExHentai }),
    center("bt:child", { name: "Gallery files", parentId: "site:1", siteTaskId: 1 }),
    center("bt:manual", { name: "Archive" }),
    center("http:file", { name: "Video" }),
  ];
  mocks.get.mockImplementation(async () => ({ code: 0, data: snapshot }));
  mocks.batch.mockImplementation(async ({ ids }: { ids: string[] }) => ({
    code: 0,
    data: ids.map((id) => ({ id, code: 0, success: true })),
  }));
  mocks.direct.mockResolvedValue({ code: 0 });
  useDownloadTasksStore.getState().setTasks([
    {
      id: 1,
      name: "Gallery",
      key: "https://example.test/gallery/123",
      thirdPartyId: ThirdPartyId.ExHentai,
      type: 1,
      progress: 0,
      downloadStatusUpdateDt: new Date(),
      status: DownloadTaskStatus.Idle,
      failureTimes: 0,
      autoRetry: false,
      availableActions: [],
      displayName: "Gallery",
      canStart: true,
      createdAt: "2026-10-19",
    },
  ]);
});
afterEach(() => {
  vi.restoreAllMocks();
  useDownloadTasksStore.getState().setTasks([]);
});
describe("unified download center", () => {
  it("uses one virtualized list, hides children until expanded, and keeps request statistics", async () => {
    mount();
    await screen.findByText("Gallery");
    expect(rows()).toEqual(["site:1", "bt:manual", "http:file"]);
    expect(screen.getByRole("listbox")).toHaveAttribute("data-item-height", "128");
    expect(screen.getByText("request-statistics")).toBeInTheDocument();
    fireEvent.click(screen.getByText("expand-1"));
    expect(rows()).toEqual(["site:1", "bt:child", "bt:manual", "http:file"]);
    fireEvent.click(screen.getByText("source-bt:child"));
    expect(mocks.portal).toHaveBeenCalledWith(expect.anything(), { id: 1 });
  });
  it("opens the BT alias destination with the linked task visible and no duplicate parent", async () => {
    mount("/downloader?category=general&provider=bt&id=bt%3Achild");
    await screen.findByText("Gallery files");
    expect(rows()).toEqual(["bt:child"]);
  });
  it("uses common task IDs for start and stop controls", async () => {
    mount();
    await screen.findByText("Archive");
    fireEvent.click(screen.getByText("Archive"));
    fireEvent.click(screen.getByText("downloader.action.startSelected"));
    await waitFor(() =>
      expect(mocks.batch).toHaveBeenCalledWith({
        ids: ["bt:manual"],
        action: "start",
        actionOnConflict: DownloadTaskActionOnConflict.NotSet,
      }),
    );
    await waitFor(() => expect(screen.getByText("downloader.action.stopSelected")).toBeEnabled());
    fireEvent.click(screen.getByText("downloader.action.stopSelected"));
    await waitFor(() =>
      expect(mocks.batch).toHaveBeenCalledWith({
        ids: ["bt:manual"],
        action: "stop",
        actionOnConflict: DownloadTaskActionOnConflict.NotSet,
      }),
    );
  });
  it("counts linked BT tasks in provider status filters without duplicating global totals", async () => {
    mount("/downloader?provider=bt");
    await screen.findByText("Gallery files");
    expect(rows()).toEqual(["bt:child", "bt:manual"]);
    expect(screen.getByText("downloader.filter.allStatuses").textContent).toBe(
      "downloader.filter.allStatuses2",
    );
    expect(screen.getByText("downloadCenter.category.all").textContent).toBe(
      "downloadCenter.category.all3",
    );
  });
  it("clears selection when provider filters hide the selected task", async () => {
    mount();
    await screen.findByText("Archive");
    fireEvent.click(screen.getByText("Archive"));
    fireEvent.change(screen.getByLabelText("downloader.filter.source"), {
      target: { value: "http" },
    });
    expect(rows()).toEqual(["http:file"]);
    expect(screen.queryByText("downloader.action.startSelected")).toBeNull();
    fireEvent.click(screen.getByText("downloadCenter.action.startVisible"));
    await waitFor(() =>
      expect(mocks.batch).toHaveBeenCalledWith({
        ids: ["http:file"],
        action: "start",
        actionOnConflict: DownloadTaskActionOnConflict.Ignore,
      }),
    );
  });
  it("keeps failed batch items visible while preserving successful operations", async () => {
    mocks.batch.mockResolvedValue({
      code: 0,
      data: [{ id: "bt:manual", success: false, code: 400, error: "Destination unavailable" }],
    });
    mount();
    await screen.findByText("Archive");
    fireEvent.click(screen.getByText("start-bt:manual"));
    expect(await screen.findByRole("alert")).toHaveTextContent("Archive: Destination unavailable");
  });
  it("retries only queue-conflicting IDs using the chosen download order", async () => {
    mocks.batch.mockResolvedValueOnce({
      code: 0,
      data: [
        { id: "site:1", success: false, code: ResponseCode.Conflict, error: "Busy queue" },
        { id: "bt:manual", success: true, code: 0 },
      ],
    });
    mount();
    await screen.findByText("Gallery");
    fireEvent.click(screen.getByText("downloadCenter.action.startVisible"));
    await waitFor(() => expect(mocks.portal).toHaveBeenCalled());
    const props = mocks.portal.mock.calls[0][1];
    await act(async () => props.onOk());
    expect(mocks.batch).toHaveBeenLastCalledWith({
      ids: ["site:1"],
      action: "start",
      actionOnConflict: DownloadTaskActionOnConflict.StopOthers,
    });
    await act(async () => props.onClose());
    expect(mocks.batch).toHaveBeenCalledTimes(2);
  });
  it.each([
    ["MacIntel", "metaKey"],
    ["Win32", "ctrlKey"],
  ])("keeps %s select-all scoped to the task list", async (platform, modifier) => {
    vi.spyOn(navigator, "platform", "get").mockReturnValue(platform);
    mount();
    await screen.findByText("Archive");
    screen.getByText("Archive").focus();
    fireEvent.keyDown(document.activeElement!, { key: "a", [modifier]: true });
    fireEvent.click(screen.getByText("downloader.action.stopSelected"));
    await waitFor(() =>
      expect(mocks.batch).toHaveBeenCalledWith(
        expect.objectContaining({ ids: ["site:1", "bt:manual", "http:file"], action: "stop" }),
      ),
    );
  });
  it("preserves direct-download conflicts and reports a failed request", async () => {
    mocks.direct
      .mockResolvedValueOnce({ code: ResponseCode.Conflict })
      .mockResolvedValueOnce({ code: 400, message: "Cookie required" });
    mount();
    await screen.findByText("Gallery");
    fireEvent.click(screen.getByText("direct-1"));
    await waitFor(() => expect(mocks.portal).toHaveBeenCalled());
    await act(async () => mocks.portal.mock.calls[0][1].onClose());
    await expect(mocks.directResult).rejects.toThrow("Cookie required");
    expect(mocks.direct).toHaveBeenLastCalledWith(1, DownloadTaskActionOnConflict.Ignore);
  });
});
