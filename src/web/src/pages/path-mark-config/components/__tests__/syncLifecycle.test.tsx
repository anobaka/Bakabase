import type { BTask } from "@/core/models/BTask";
import type * as ReactI18next from "react-i18next";

import React from "react";
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import PendingSyncButton from "../PendingSyncButton";
import PendingSyncListModal from "../PendingSyncListModal";
import SyncProgressModal from "../SyncProgressModal";
import { PATH_MARK_SYNC_TASK_ID, RESOURCE_SYNC_TASK_ID } from "../pathMarkSyncTask";

import { useBTasksStore } from "@/stores/bTasks";
import { usePathMarksStore } from "@/stores/pathMarks";
import { BTaskStatus, PathMarkSyncStatus, PathMarkType } from "@/sdk/constants";

const api = vi.hoisted(() => ({
  pending: vi.fn(),
  count: vi.fn(),
  all: vi.fn(),
  start: vi.fn(),
  force: vi.fn(),
  startOne: vi.fn(),
  pause: vi.fn(),
  resume: vi.fn(),
  stop: vi.fn(),
  portal: vi.fn(),
}));

vi.mock("@/sdk/BApi", () => ({
  default: {
    pathMark: {
      getPendingPathMarks: api.pending,
      getPendingPathMarksCount: api.count,
      getAllPathMarks: api.all,
      startPathMarkSyncAll: api.start,
      forceResyncAllPathMarks: api.force,
      startPathMarkSync: api.startOne,
    },
    backgroundTask: {
      pauseBackgroundTask: api.pause,
      resumeBackgroundTask: api.resume,
      stopBackgroundTask: api.stop,
    },
  },
}));
vi.mock("react-i18next", async (importOriginal) => ({
  ...(await importOriginal<typeof ReactI18next>()),
  useTranslation: () => ({ t: (key: string) => key }),
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: api.portal }),
}));
vi.mock("@/components/HelpCenter", () => ({ HelpCenterButton: () => null }));
vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onPress, isDisabled }: any) => (
    <button disabled={isDisabled} onClick={onPress}>
      {children}
    </button>
  ),
  Badge: ({ children, content }: any) => (
    <div>
      <span data-testid="pending-count">{content}</span>
      {children}
    </div>
  ),
  Chip: ({ children }: any) => <span>{children}</span>,
  Tooltip: ({ children }: any) => <>{children}</>,
  CircularProgress: () => <span>progress</span>,
  Spinner: () => <span>loading</span>,
  Modal: ({ children, footer, title, visible = true }: any) =>
    visible ? (
      <div>
        {title}
        {children}
        {footer}
      </div>
    ) : null,
}));
vi.mock("@heroui/react", () => ({
  Modal: ({ children, isOpen }: any) => (isOpen ? <div>{children}</div> : null),
  ModalBody: ({ children }: any) => <div>{children}</div>,
  ModalContent: ({ children }: any) => <div>{children}</div>,
  ModalHeader: ({ children }: any) => <div>{children}</div>,
  ModalFooter: ({ children }: any) => <div>{children}</div>,
  Progress: () => <span>progress</span>,
}));

const mark = (id: number, type = PathMarkType.Resource) => ({
  id,
  type,
  path: `/data/${id}`,
  syncStatus: PathMarkSyncStatus.PendingDelete,
  isDeleted: true,
});
const task = (id: string, status: BTaskStatus, run = "old") =>
  ({
    id,
    status,
    startedAt: run,
    createdAt: "persistent",
    percentage: 100,
  }) as BTask;
const deferred = () => {
  let resolve!: (value: any) => void;
  const promise = new Promise<any>((done) => {
    resolve = done;
  });

  return { promise, resolve };
};
const push = async (...tasks: BTask[]) =>
  act(async () => {
    useBTasksStore.getState().setTasks(tasks);
  });

beforeEach(() => {
  vi.clearAllMocks();
  useBTasksStore.getState().setTasks([]);
  usePathMarksStore.setState({ marks: new Map(), deletedMarkIds: new Set() });
  api.pending.mockResolvedValue({ code: 0, data: [mark(1)] });
  api.all.mockResolvedValue({ code: 0, data: [] });
  api.count.mockResolvedValue({ code: 0, data: 1 });
  api.start.mockResolvedValue({ code: 0 });
  api.force.mockResolvedValue({ code: 0 });
  api.pause.mockResolvedValue({ code: 0 });
  api.resume.mockResolvedValue({ code: 0 });
  api.stop.mockResolvedValue({ code: 0 });
});
afterEach(cleanup);

describe("authoritative pending snapshots", () => {
  it("refreshes an open list after a resource run completes without an observed running state", async () => {
    useBTasksStore.getState().setTasks([task(RESOURCE_SYNC_TASK_ID, BTaskStatus.Completed)]);
    render(<PendingSyncListModal />);
    await screen.findByText("/data/1");
    api.pending.mockResolvedValue({ code: 0, data: [] });
    await push(task(RESOURCE_SYNC_TASK_ID, BTaskStatus.Completed, "new"));
    await screen.findByText("pathMarkConfig.status.allSynced");
    expect(screen.queryByText("/data/1")).toBeNull();
    expect(api.pending).toHaveBeenCalledTimes(2);
  });

  it("ignores a pre-completion request arriving after the refreshed empty list", async () => {
    const old = deferred();

    api.pending.mockImplementationOnce(() => old.promise);
    render(<PendingSyncListModal />);
    await waitFor(() => expect(api.pending).toHaveBeenCalledTimes(1));
    api.pending.mockResolvedValue({ code: 0, data: [] });
    await push(task(PATH_MARK_SYNC_TASK_ID, BTaskStatus.Completed, "fast"));
    await screen.findByText("pathMarkConfig.status.allSynced");
    await act(async () => old.resolve({ code: 0, data: [mark(1)] }));
    expect(screen.queryByText("/data/1")).toBeNull();
  });

  it("does not resurrect a physically deleted mark from an API snapshot or a later stale reply", async () => {
    render(<PendingSyncListModal />);
    await screen.findByText("/data/1");
    api.pending.mockResolvedValue({ code: 0, data: [mark(1)] });
    await act(async () => usePathMarksStore.getState().removeMark(1));
    await screen.findByText("pathMarkConfig.status.allSynced");
    expect(screen.queryByText("/data/1")).toBeNull();
    expect(screen.getByText("pathMarkConfig.action.syncPending").closest("button")).toBeDisabled();
  });

  it("uses a fresh pending API status instead of an older synced store mark", async () => {
    usePathMarksStore
      .getState()
      .updateMark({ ...mark(1), syncStatus: PathMarkSyncStatus.Synced } as any);
    render(<PendingSyncListModal />);
    await screen.findByText("/data/1");
    await act(async () =>
      usePathMarksStore
        .getState()
        .updateMark({ ...mark(1), syncStatus: PathMarkSyncStatus.Synced } as any),
    );
    await screen.findByText("pathMarkConfig.status.allSynced");
  });

  it("refreshes the badge for either pipeline and prevents older count responses restoring its value", async () => {
    const old = deferred();
    const completed = vi.fn();

    api.count.mockImplementationOnce(() => old.promise);
    render(<PendingSyncButton onSyncComplete={completed} />);
    api.count.mockResolvedValue({ code: 0, data: 0 });
    await push(task(RESOURCE_SYNC_TASK_ID, BTaskStatus.Completed, "fast"));
    await waitFor(() => expect(api.count).toHaveBeenCalledTimes(2));
    await act(async () => old.resolve({ code: 0, data: 3 }));
    expect(screen.getByTestId("pending-count")).toHaveTextContent("0");
    expect(completed).toHaveBeenCalledTimes(1);
    await push(task(PATH_MARK_SYNC_TASK_ID, BTaskStatus.Running, "new"));
    await push(task(PATH_MARK_SYNC_TASK_ID, BTaskStatus.Error, "new"));
    await waitFor(() => expect(api.count).toHaveBeenCalledTimes(3));
  });
});

describe("this synchronization request", () => {
  it.each([BTaskStatus.Completed, BTaskStatus.Running])(
    "waits for a full initial task snapshot before submitting and never owns historical status %s",
    async (status) => {
      const completed = vi.fn();

      useBTasksStore.getState().resetTasks();
      render(<SyncProgressModal onComplete={completed} />);
      await screen.findByText("pathMarkConfig.status.waitingForConnection");
      expect(api.start).not.toHaveBeenCalled();
      expect(api.pending).not.toHaveBeenCalled();
      expect(screen.getByText("common.action.close")).toBeInTheDocument();
      await push(task(RESOURCE_SYNC_TASK_ID, status, "historical"));
      await waitFor(() => expect(api.start).toHaveBeenCalledTimes(1));
      expect(completed).not.toHaveBeenCalled();
      expect(screen.queryByText("pathMarkConfig.status.completed")).toBeNull();
      expect(screen.getByText("pathMarkConfig.action.stop").closest("button")).toBeDisabled();
      expect(screen.getByText("pathMarkConfig.action.pause").closest("button")).toBeDisabled();
      await push(task(RESOURCE_SYNC_TASK_ID, BTaskStatus.Completed, "mine"));
      await screen.findByText("pathMarkConfig.status.completed");
      expect(completed).toHaveBeenCalledTimes(1);
    },
  );

  it("can close while waiting for the initial snapshot without submitting work afterwards", async () => {
    const destroyed = vi.fn();

    useBTasksStore.getState().resetTasks();
    const view = render(<SyncProgressModal onDestroyed={destroyed} />);

    fireEvent.click(await screen.findByText("common.action.close"));
    expect(destroyed).toHaveBeenCalledTimes(1);
    view.unmount();
    await push(task(RESOURCE_SYNC_TASK_ID, BTaskStatus.Completed, "history"));
    expect(api.start).not.toHaveBeenCalled();
  });

  it.each([true, false])(
    "settles a lost monitor without success or control of a replacement run (%s)",
    async (replaced) => {
      const completed = vi.fn();

      render(<SyncProgressModal onComplete={completed} />);
      await waitFor(() => expect(api.start).toHaveBeenCalledTimes(1));
      await push({
        ...task(RESOURCE_SYNC_TASK_ID, BTaskStatus.Running, "mine"),
        createdAt: "run-a",
      });
      const before = api.pending.mock.calls.length;

      await push(
        ...(replaced
          ? [
              {
                ...task(RESOURCE_SYNC_TASK_ID, BTaskStatus.Running, "unrelated"),
                createdAt: "run-b",
              },
            ]
          : []),
      );
      await screen.findByText("pathMarkConfig.status.progressUnavailable");
      await waitFor(() => expect(api.pending.mock.calls.length).toBeGreaterThan(before));
      expect(screen.queryByText("pathMarkConfig.status.completed")).toBeNull();
      expect(screen.queryByText("pathMarkConfig.action.pause")).toBeNull();
      expect(screen.queryByText("pathMarkConfig.action.stop")).toBeNull();
      expect(screen.getByText("common.action.close")).toBeInTheDocument();
      expect(completed).toHaveBeenCalledTimes(1);
      expect(api.pause).not.toHaveBeenCalled();
      expect(api.stop).not.toHaveBeenCalled();
    },
  );

  it("rechecks run ownership after a delayed stop confirmation", async () => {
    render(<SyncProgressModal />);
    await waitFor(() => expect(api.start).toHaveBeenCalledTimes(1));
    await push({ ...task(RESOURCE_SYNC_TASK_ID, BTaskStatus.Running, "mine"), createdAt: "run-a" });
    api.stop.mockResolvedValueOnce({ code: 202, message: "interrupt?" });
    fireEvent.click(screen.getByText("pathMarkConfig.action.stop"));
    await waitFor(() => expect(api.portal).toHaveBeenCalledTimes(1));
    await push({
      ...task(RESOURCE_SYNC_TASK_ID, BTaskStatus.Running, "other"),
      createdAt: "run-b",
    });
    await act(async () => api.portal.mock.calls[0][1].onOk());
    expect(api.stop).toHaveBeenCalledTimes(1);
  });

  it("does not turn an unknown lost outcome into success when reconciliation finds no pending marks", async () => {
    render(<SyncProgressModal />);
    await waitFor(() => expect(api.start).toHaveBeenCalledTimes(1));
    await push({ ...task(RESOURCE_SYNC_TASK_ID, BTaskStatus.Running, "mine"), createdAt: "run-a" });
    api.pending.mockResolvedValue({ code: 0, data: [] });
    api.all.mockResolvedValue({
      code: 0,
      data: [{ ...mark(1), syncStatus: PathMarkSyncStatus.Synced }],
    });
    await push();
    await waitFor(() => expect(api.all).toHaveBeenCalled());
    expect(screen.getByText("pathMarkConfig.status.progressUnavailable")).toBeInTheDocument();
    expect(screen.queryByText("pathMarkConfig.status.completed")).toBeNull();
    expect(screen.getByText("common.action.close")).toBeInTheDocument();
  });

  it.each([RESOURCE_SYNC_TASK_ID, PATH_MARK_SYNC_TASK_ID])(
    "waits for the requested %s run after an existing unrelated run finishes",
    async (id) => {
      const completed = vi.fn();
      const type = id === RESOURCE_SYNC_TASK_ID ? PathMarkType.Resource : PathMarkType.Property;

      api.pending.mockResolvedValue({ code: 0, data: [mark(1, type)] });
      useBTasksStore.getState().setTasks([task(id, BTaskStatus.Running)]);
      render(<SyncProgressModal onComplete={completed} />);
      await waitFor(() => expect(api.start).toHaveBeenCalledTimes(1));
      await waitFor(() => expect(api.pending).toHaveBeenCalledTimes(2));
      expect(screen.getByText("pathMarkConfig.action.pause").closest("button")).toBeDisabled();
      expect(screen.getByText("pathMarkConfig.action.stop").closest("button")).toBeDisabled();
      await push(task(id, BTaskStatus.Completed));
      expect(completed).not.toHaveBeenCalled();
      expect(screen.queryByText("pathMarkConfig.status.completed")).toBeNull();
      await push(task(id, BTaskStatus.Running, "new"));
      fireEvent.click(screen.getByText("pathMarkConfig.action.pause"));
      await waitFor(() => expect(api.pause).toHaveBeenCalledWith(id));
      await push(task(id, BTaskStatus.Completed, "new"));
      await screen.findByText("pathMarkConfig.status.completed");
      expect(completed).toHaveBeenCalledTimes(1);
    },
  );

  it("finishes an empty request independently of an unrelated resource job", async () => {
    api.pending.mockResolvedValue({ code: 0, data: [] });
    useBTasksStore.getState().setTasks([task(RESOURCE_SYNC_TASK_ID, BTaskStatus.Running)]);
    const completed = vi.fn();

    render(<SyncProgressModal onComplete={completed} />);
    await screen.findByText("pathMarkConfig.status.completed");
    expect(completed).toHaveBeenCalledTimes(1);
    expect(api.stop).not.toHaveBeenCalled();
    expect(api.pause).not.toHaveBeenCalled();
  });

  it("can close a queued monitor without controlling the unrelated run or cancelling the request", async () => {
    const closed = vi.fn();
    const destroyed = vi.fn();

    useBTasksStore.getState().setTasks([task(RESOURCE_SYNC_TASK_ID, BTaskStatus.Running)]);
    render(<SyncProgressModal onClose={closed} onDestroyed={destroyed} />);
    await waitFor(() => expect(api.pending).toHaveBeenCalledTimes(2));
    const close = await screen.findByText("common.action.close");

    expect(screen.getByText("pathMarkConfig.action.pause").closest("button")).toBeDisabled();
    expect(screen.getByText("pathMarkConfig.action.stop").closest("button")).toBeDisabled();
    fireEvent.click(close);
    expect(closed).toHaveBeenCalledTimes(1);
    expect(destroyed).toHaveBeenCalledTimes(1);
    expect(api.stop).not.toHaveBeenCalled();
    expect(api.pause).not.toHaveBeenCalled();
    expect(api.start).toHaveBeenCalledTimes(1);
  });

  it("does not treat a resource mark's early Synced status as completion while the old resource run is still writing", async () => {
    const completed = vi.fn();

    useBTasksStore.getState().setTasks([task(RESOURCE_SYNC_TASK_ID, BTaskStatus.Running)]);
    api.pending
      .mockResolvedValueOnce({ code: 0, data: [mark(1)] })
      .mockResolvedValue({ code: 0, data: [] });
    api.all.mockResolvedValue({
      code: 0,
      data: [{ ...mark(1), syncStatus: PathMarkSyncStatus.Synced }],
    });
    render(<SyncProgressModal onComplete={completed} />);
    await waitFor(() => expect(api.all).toHaveBeenCalledTimes(1));
    expect(completed).not.toHaveBeenCalled();
    expect(screen.queryByText("pathMarkConfig.status.completed")).toBeNull();
    await push(task(RESOURCE_SYNC_TASK_ID, BTaskStatus.Completed));
    await screen.findByText("pathMarkConfig.status.completed");
    expect(completed).toHaveBeenCalledTimes(1);
  });

  it("keeps this request completed when a later unrelated run reuses the same task id", async () => {
    const completed = vi.fn();

    render(<SyncProgressModal onComplete={completed} />);
    await waitFor(() => expect(api.start).toHaveBeenCalledTimes(1));
    await push(task(RESOURCE_SYNC_TASK_ID, BTaskStatus.Completed, "mine"));
    await screen.findByText("pathMarkConfig.status.completed");
    await push(task(RESOURCE_SYNC_TASK_ID, BTaskStatus.Running, "later"));
    expect(screen.getByText("pathMarkConfig.status.completed")).toBeInTheDocument();
    expect(screen.queryByText("pathMarkConfig.action.stop")).toBeNull();
    expect(screen.queryByText("pathMarkConfig.action.pause")).toBeNull();
    expect(completed).toHaveBeenCalledTimes(1);
  });

  it("accepts a genuine no-op when marks disappear before submission instead of waiting forever", async () => {
    api.pending
      .mockResolvedValueOnce({ code: 0, data: [mark(1)] })
      .mockResolvedValue({ code: 0, data: [] });
    api.all.mockResolvedValue({ code: 0, data: [] });
    const completed = vi.fn();

    render(<SyncProgressModal onComplete={completed} />);
    await screen.findByText("pathMarkConfig.status.completed");
    expect(api.all).toHaveBeenCalledTimes(1);
    expect(completed).toHaveBeenCalledTimes(1);
  });

  it("does not mistake a mark absent from pending because it is actively syncing for a no-op", async () => {
    api.pending
      .mockResolvedValueOnce({ code: 0, data: [mark(1)] })
      .mockResolvedValue({ code: 0, data: [] });
    api.all.mockResolvedValue({
      code: 0,
      data: [{ ...mark(1), syncStatus: PathMarkSyncStatus.Syncing }],
    });
    const completed = vi.fn();

    render(<SyncProgressModal onComplete={completed} />);
    await waitFor(() => expect(api.all).toHaveBeenCalledTimes(1));
    expect(completed).not.toHaveBeenCalled();
    await push(task(RESOURCE_SYNC_TASK_ID, BTaskStatus.Completed, "fast"));
    await screen.findByText("pathMarkConfig.status.completed");
    expect(completed).toHaveBeenCalledTimes(1);
  });

  it("does not show a previous successful run while awaiting this request and accepts a fast new completion", async () => {
    const trigger = deferred();
    const completed = vi.fn();

    api.start.mockImplementationOnce(() => trigger.promise);
    useBTasksStore.getState().setTasks([task(RESOURCE_SYNC_TASK_ID, BTaskStatus.Completed)]);
    render(<SyncProgressModal onComplete={completed} />);
    await waitFor(() => expect(api.start).toHaveBeenCalledTimes(1));
    expect(screen.queryByText("pathMarkConfig.status.completed")).toBeNull();
    expect(completed).not.toHaveBeenCalled();
    await push(task(RESOURCE_SYNC_TASK_ID, BTaskStatus.Completed, "new"));
    expect(completed).not.toHaveBeenCalled();
    await act(async () => trigger.resolve({ code: 0 }));
    await screen.findByText("pathMarkConfig.status.completed");
    expect(completed).toHaveBeenCalledTimes(1);
  });

  it("waits for both pipelines and addresses the actual resource task for pause/resume/stop", async () => {
    const completed = vi.fn();

    api.pending.mockResolvedValue({ code: 0, data: [mark(1), mark(2, PathMarkType.Property)] });
    render(<SyncProgressModal onComplete={completed} />);
    await waitFor(() => expect(api.start).toHaveBeenCalledTimes(1));
    await push(
      task(RESOURCE_SYNC_TASK_ID, BTaskStatus.Running, "new"),
      task(PATH_MARK_SYNC_TASK_ID, BTaskStatus.NotStarted, "new"),
    );
    fireEvent.click(screen.getByText("pathMarkConfig.action.pause"));
    await waitFor(() => expect(api.pause).toHaveBeenCalledWith(RESOURCE_SYNC_TASK_ID));
    fireEvent.click(screen.getByText("pathMarkConfig.action.stop"));
    await waitFor(() => expect(api.stop).toHaveBeenCalledWith(RESOURCE_SYNC_TASK_ID));
    expect(api.stop).toHaveBeenCalledWith(PATH_MARK_SYNC_TASK_ID);
    await push(
      task(RESOURCE_SYNC_TASK_ID, BTaskStatus.Completed, "new"),
      task(PATH_MARK_SYNC_TASK_ID, BTaskStatus.Paused, "new"),
    );
    expect(completed).not.toHaveBeenCalled();
    fireEvent.click(screen.getByText("pathMarkConfig.action.resume"));
    await waitFor(() => expect(api.resume).toHaveBeenCalledWith(PATH_MARK_SYNC_TASK_ID));
    await push(
      task(RESOURCE_SYNC_TASK_ID, BTaskStatus.Completed, "new"),
      task(PATH_MARK_SYNC_TASK_ID, BTaskStatus.Completed, "new"),
    );
    await screen.findByText("pathMarkConfig.status.completed");
    expect(completed).toHaveBeenCalledTimes(1);
  });

  it("finishes a real no-op without inventing a property synchronization task", async () => {
    api.pending.mockResolvedValue({ code: 0, data: [] });
    const completed = vi.fn();

    render(<SyncProgressModal onComplete={completed} />);
    await screen.findByText("pathMarkConfig.status.completed");
    expect(api.start).toHaveBeenCalledTimes(1);
    expect(completed).toHaveBeenCalledTimes(1);
    expect(screen.getByText("100%")).toBeInTheDocument();
  });

  it.each([true, false])(
    "can resume a paused resource stage while the property stage is queued or not yet received (%s)",
    async (propertyQueued) => {
      api.pending.mockResolvedValue({ code: 0, data: [mark(1), mark(2, PathMarkType.Property)] });
      render(<SyncProgressModal />);
      await waitFor(() => expect(api.start).toHaveBeenCalledTimes(1));
      await push(
        task(RESOURCE_SYNC_TASK_ID, BTaskStatus.Paused, "new"),
        ...(propertyQueued ? [task(PATH_MARK_SYNC_TASK_ID, BTaskStatus.NotStarted, "new")] : []),
      );
      fireEvent.click(screen.getByText("pathMarkConfig.action.resume"));
      await waitFor(() => expect(api.resume).toHaveBeenCalledWith(RESOURCE_SYNC_TASK_ID));
      expect(api.resume).toHaveBeenCalledTimes(1);
      expect(screen.queryByText("pathMarkConfig.status.completed")).toBeNull();
    },
  );

  it("shows a start error rather than old success and refreshes the authoritative result", async () => {
    const completed = vi.fn();

    useBTasksStore.getState().setTasks([task(RESOURCE_SYNC_TASK_ID, BTaskStatus.Completed)]);
    api.start.mockRejectedValueOnce(new Error("start refused"));
    render(<SyncProgressModal onComplete={completed} />);
    await screen.findByText("start refused");
    expect(screen.queryByText("pathMarkConfig.status.completed")).toBeNull();
    expect(completed).toHaveBeenCalledTimes(1);
  });

  it("honors the backend stop confirmation before cancelling a resource task", async () => {
    render(<SyncProgressModal />);
    await waitFor(() => expect(api.start).toHaveBeenCalledTimes(1));
    await push(task(RESOURCE_SYNC_TASK_ID, BTaskStatus.Running, "new"));
    api.stop.mockResolvedValueOnce({ code: 202, message: "interruption" });
    fireEvent.click(screen.getByText("pathMarkConfig.action.stop"));
    await waitFor(() => expect(api.portal).toHaveBeenCalledTimes(1));
    expect(api.stop).toHaveBeenCalledTimes(1);
    await act(async () => api.portal.mock.calls[0][1].onOk());
    expect(api.stop).toHaveBeenLastCalledWith(RESOURCE_SYNC_TASK_ID, { confirm: true });
  });
});
