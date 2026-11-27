import type { ReactElement, ReactNode } from "react";
import { act, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { cloneElement, useState } from "react";
import DirectoryTaskStatus, { directoryTasks } from "../components/DirectoryTaskStatus";
import { useBTasksStore } from "@/stores/bTasks";
import { BTaskResourceType, BTaskStatus, BTaskType } from "@/sdk/constants";
import type { BTask } from "@/core/models/BTask";
vi.mock("@/components/FloatingAssistant/components/TaskTable", () => ({
  TaskTable: ({ tasks }: { tasks: BTask[] }) => <div>{tasks.map(task => <p key={task.id}>{task.name}: {task.error ?? `${task.percentage}%`}</p>)}</div>,
}));
vi.mock("@/components/bakaui", () => ({
  Spinner: () => <span>loading</span>,
  Button: ({ children, onPress, color, startContent }: any) => <button onClick={onPress} data-color={color}>{startContent}{children}</button>,
  Popover: ({ children, trigger }: { trigger: ReactElement<{ onPress?: () => void }>; children: ReactNode }) => {
    const [open, setOpen] = useState(false);

    return <>{cloneElement(trigger, { onPress: () => setOpen(!open) })}{open && children}</>;
  },
}));
const task = (patch: Partial<BTask> = {}) => ({ id: "decompress", name: "Extract renamed archive",
  resourceType: BTaskResourceType.FileSystemEntry, type: BTaskType.Decompress,
  resourceKeys: ["/work/archive-old.zip"], status: BTaskStatus.Running, percentage: 37,
  isPersistent: false, createdAt: "2026-11-11T01:00:00Z", ...patch } as BTask);
afterEach(() => act(() => useBTasksStore.getState().setTasks([])));
it("keeps a renamed or removed source's progress/error accessible using the existing task table and no filesystem lookup", async () => {
  useBTasksStore.getState().setTasks([task()]);
  render(<DirectoryTaskStatus path="/work" />);
  fireEvent.click(screen.getByRole("button", { name: /fileExplorer.tasks.current/ }));
  expect(await screen.findByText("Extract renamed archive: 37%")).toBeVisible();
  act(() => useBTasksStore.getState().updateTask(task({ status: BTaskStatus.Error, error: "Incorrect archive password" })));
  expect(screen.getByText("Extract renamed archive: Incorrect archive password")).toBeVisible();
  expect(screen.getByRole("button")).toHaveAttribute("data-color", "danger");
});
it.each([BTaskStatus.Paused, BTaskStatus.WaitingForInput, BTaskStatus.NotStarted])("does not imply active work for status %s", status => {
  useBTasksStore.getState().setTasks([task({ status })]);
  render(<DirectoryTaskStatus path="/work" />);
  expect(screen.getByRole("button", { name: "fileExplorer.tasks.current" })).toBeVisible();
  expect(screen.queryByText("loading")).toBeNull();
});
it("ignores unrelated roots and resources and supersedes an old error with a successful retry", () => {
  const failed = task({ status: BTaskStatus.Error });
  expect(directoryTasks([failed, task({ id: "other", resourceKeys: ["/work-other/archive.zip"] }),
    task({ id: "resource", resourceType: BTaskResourceType.Resource })], "/work/")).toEqual([failed]);
  expect(directoryTasks([failed, task({ id: "retry", status: BTaskStatus.Completed, createdAt: "2026-11-11T02:00:00Z" })], "/work")).toEqual([]);
});
