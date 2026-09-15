import type { ReactNode } from "react";
import type { DownloadTask } from "@/core/models/DownloadTask";

import { act } from "react-dom/test-utils";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import ActivityOverview from "../ActivityOverview";

import { DownloadTaskStatus, ThirdPartyId } from "@/sdk/constants";
import { useDownloadTasksStore } from "@/stores/downloadTasks";

const { navigate } = vi.hoisted(() => ({ navigate: vi.fn() }));

vi.mock("react-router-dom", () => ({ useNavigate: () => navigate }));
vi.mock("@/components/bakaui", () => ({
  Card: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  CardBody: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  Chip: ({ children }: { children: ReactNode }) => <span>{children}</span>,
  Button: ({ children, onPress }: { children: ReactNode; onPress: () => void }) => (
    <button onClick={onPress}>{children}</button>
  ),
}));

const task = (id: number, status: DownloadTaskStatus): DownloadTask => ({
  id,
  key: `task-${id}`,
  thirdPartyId: ThirdPartyId.ExHentai,
  type: 1,
  progress: 0,
  downloadStatusUpdateDt: new Date(),
  status,
  failureTimes: 0,
  autoRetry: false,
  availableActions: [],
  displayName: `Task ${id}`,
  canStart: false,
  createdAt: new Date().toISOString(),
});

let container: HTMLDivElement;
let root: Root;

const countFor = (key: string) =>
  Array.from(container.querySelectorAll("dt"))
    .find((node) => node.textContent === `dashboard.activity.${key}`)!
    .parentElement!.querySelector("dd");

const syncingStatus = () => container.querySelector('[role="status"]');

function renderOverview() {
  act(() =>
    root.render(
      <ActivityOverview workflows={{ runningCount: 8, waitingCount: 2, failedRecentlyCount: 3 }} />,
    ),
  );
}

beforeEach(() => {
  navigate.mockClear();
  useDownloadTasksStore.setState({ tasks: [], initialized: false });
  (globalThis as any).IS_REACT_ACT_ENVIRONMENT = true;
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
});

describe("dashboard task overview", () => {
  it("does not turn an unsynchronized empty list or an incremental push into a known zero", () => {
    renderOverview();
    expect(countFor("downloader.active")).toHaveTextContent("—");
    expect(countFor("downloader.failed")).toHaveTextContent("—");
    expect(syncingStatus()).toHaveTextContent("dashboard.activity.downloader.awaitingSync");

    act(() => useDownloadTasksStore.getState().updateTask(task(1, DownloadTaskStatus.Downloading)));
    expect(useDownloadTasksStore.getState().initialized).toBe(false);
    expect(countFor("downloader.active")).toHaveTextContent("—");
    expect(countFor("workflows.running")).toHaveTextContent("8");
  });

  it("counts only active and failed downloader tasks and keeps them separate from workflow totals", () => {
    useDownloadTasksStore
      .getState()
      .setTasks([
        task(1, DownloadTaskStatus.InQueue),
        task(2, DownloadTaskStatus.Starting),
        task(3, DownloadTaskStatus.Downloading),
        task(4, DownloadTaskStatus.Stopping),
        task(5, DownloadTaskStatus.Failed),
        task(6, DownloadTaskStatus.Complete),
        task(7, DownloadTaskStatus.Idle),
        task(8, DownloadTaskStatus.Disabled),
      ]);
    renderOverview();
    expect(countFor("downloader.active")).toHaveTextContent(/^4$/);
    expect(countFor("downloader.failed")).toHaveTextContent(/^1$/);
    expect(countFor("workflows.running")).toHaveTextContent(/^8$/);
    expect(countFor("workflows.waiting")).toHaveTextContent(/^2$/);
    expect(countFor("workflows.failedRecently")).toHaveTextContent(/^3$/);
    expect(syncingStatus()).not.toBeInTheDocument();

    act(() =>
      useDownloadTasksStore
        .getState()
        .updateTasks([
          task(3, DownloadTaskStatus.Complete),
          task(5, DownloadTaskStatus.Downloading),
        ]),
    );
    expect(countFor("downloader.active")).toHaveTextContent(/^4$/);
    expect(countFor("downloader.failed")).toHaveTextContent(/^0$/);
  });

  it("hides stale counts until a new full snapshot arrives, including a confirmed empty snapshot", () => {
    useDownloadTasksStore.getState().setTasks([task(1, DownloadTaskStatus.Failed)]);
    renderOverview();
    expect(countFor("downloader.failed")).toHaveTextContent(/^1$/);

    act(() => useDownloadTasksStore.getState().resetInitialization());
    expect(useDownloadTasksStore.getState().tasks).toHaveLength(1);
    expect(countFor("downloader.failed")).toHaveTextContent("—");
    act(() =>
      useDownloadTasksStore.getState().updateTasks([task(2, DownloadTaskStatus.Downloading)]),
    );
    expect(countFor("downloader.active")).toHaveTextContent("—");

    act(() => useDownloadTasksStore.getState().setTasks([]));
    expect(countFor("downloader.active")).toHaveTextContent(/^0$/);
    expect(countFor("downloader.failed")).toHaveTextContent(/^0$/);
    expect(syncingStatus()).not.toBeInTheDocument();
  });

  it("offers separate navigation to the workflow and downloader pages", () => {
    renderOverview();
    const button = (key: string) =>
      Array.from(container.querySelectorAll("button")).find(
        (node) => node.textContent === `dashboard.activity.${key}.action`,
      )!;

    act(() => button("workflows").click());
    expect(navigate).toHaveBeenLastCalledWith("/workflows");
    act(() => button("downloader").click());
    expect(navigate).toHaveBeenLastCalledWith("/downloader");
  });
});
