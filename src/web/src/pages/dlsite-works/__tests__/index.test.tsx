import type { BTask } from "@/core/models/BTask";

import { HeroUIProvider } from "@heroui/react";
import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import DLsiteWorksPage from "..";
import { DOWNLOAD_TASK_ID_PREFIX, EXTRACT_TASK_ID_PREFIX } from "../types";

import { BTaskResourceType, BTaskStatus, BTaskType } from "@/sdk/constants";
import { useBTasksStore } from "@/stores/bTasks";

const { getWorks, options } = vi.hoisted(() => ({
  getWorks: vi.fn(),
  options: {
    data: { accounts: [{ account: "test" }], defaultPath: "/downloads", scanFolders: [] },
    initialized: true,
    patch: vi.fn(),
  },
}));

vi.mock("@/sdk/BApi", () => ({
  default: { dlsiteWork: { getAllDLsiteWorks: getWorks } },
}));
vi.mock("@/stores/options", () => ({
  useDLsiteOptionsStore: (selector: (state: typeof options) => unknown) => selector(options),
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: vi.fn() }),
}));
vi.mock("@/components/ThirdPartyConfig", () => ({ DLsiteConfig: () => null }));
vi.mock("@/components/ConfirmModal", () => ({ default: () => null }));
vi.mock("@/components/bakaui", () => ({ toast: { success: vi.fn(), danger: vi.fn() } }));
vi.mock("../components/DLsiteTable", () => ({
  DLsiteTable: () => <div data-testid="dlsite-table" />,
}));

const task = (id: string, status: BTaskStatus, patch: Partial<BTask> = {}): BTask => ({
  id,
  name: id,
  status,
  createdAt: "2026-10-01T00:00:00Z",
  isPersistent: false,
  type: BTaskType.Download,
  resourceType: BTaskResourceType.FileSystemEntry,
  ...patch,
});

const mountPage = async () => {
  await act(async () => {
    render(
      <HeroUIProvider>
        <DLsiteWorksPage />
      </HeroUIProvider>,
    );
  });
  expect(screen.getByTestId("dlsite-table")).toBeInTheDocument();
};

const updateTask = async (nextTask: BTask) => {
  await act(async () => useBTasksStore.getState().updateTask(nextTask));
};

beforeEach(() => {
  vi.clearAllMocks();
  useBTasksStore.getState().setTasks([]);
  getWorks.mockResolvedValue({ code: 0, data: [], totalCount: 60 });
});

afterEach(() => {
  cleanup();
  useBTasksStore.getState().setTasks([]);
});

describe("DLsite task completion refresh", () => {
  it("loads once on mount when historical downloads and extracts are already complete", async () => {
    useBTasksStore
      .getState()
      .setTasks([
        task(`${DOWNLOAD_TASK_ID_PREFIX}RJ001`, BTaskStatus.Completed),
        task(`${EXTRACT_TASK_ID_PREFIX}RJ001`, BTaskStatus.Completed),
      ]);

    await mountPage();

    expect(getWorks).toHaveBeenCalledTimes(1);
  });

  it.each([DOWNLOAD_TASK_ID_PREFIX, EXTRACT_TASK_ID_PREFIX])(
    "refreshes a completed %s task once and ignores unrelated progress and duplicate pushes",
    async (prefix) => {
      const running = task(`${prefix}RJ001`, BTaskStatus.Running);

      useBTasksStore.getState().setTasks([running]);
      await mountPage();

      const completed = { ...running, status: BTaskStatus.Completed };

      await updateTask(completed);
      expect(getWorks).toHaveBeenCalledTimes(2);

      await updateTask(task("UnrelatedTask", BTaskStatus.Running, { percentage: 10 }));
      await updateTask(task("UnrelatedTask", BTaskStatus.Running, { percentage: 20 }));
      await updateTask({ ...completed });

      expect(getWorks).toHaveBeenCalledTimes(2);

      await updateTask(running);
      expect(getWorks).toHaveBeenCalledTimes(2);
      await updateTask(completed);
      expect(getWorks).toHaveBeenCalledTimes(3);
    },
  );

  it.each([DOWNLOAD_TASK_ID_PREFIX, EXTRACT_TASK_ID_PREFIX])(
    "refreshes a new completed %s task even when no running push was observed",
    async (prefix) => {
      await mountPage();

      const completed = task(`${prefix}RJ001`, BTaskStatus.Completed);

      await updateTask(completed);
      await updateTask({ ...completed });

      expect(getWorks).toHaveBeenCalledTimes(2);
    },
  );

  it("refreshes fast reruns with the same id when createdAt or startedAt changes", async () => {
    const completed = task(`${DOWNLOAD_TASK_ID_PREFIX}RJ001`, BTaskStatus.Completed, {
      startedAt: "2026-10-01T00:00:01Z",
    });

    useBTasksStore.getState().setTasks([completed]);
    await mountPage();

    const recreated = { ...completed, createdAt: "2026-10-01T01:00:00Z" };

    await updateTask(recreated);
    expect(getWorks).toHaveBeenCalledTimes(2);

    const restarted = { ...recreated, startedAt: "2026-10-01T02:00:00Z" };

    await updateTask(restarted);
    await updateTask({ ...restarted });
    expect(getWorks).toHaveBeenCalledTimes(3);
  });

  it("refreshes once when multiple downloads and extracts finish in the same batch", async () => {
    const tasks = [
      task(`${DOWNLOAD_TASK_ID_PREFIX}RJ001`, BTaskStatus.Running),
      task(`${EXTRACT_TASK_ID_PREFIX}RJ002`, BTaskStatus.Running),
    ];

    useBTasksStore.getState().setTasks(tasks);
    await mountPage();

    await act(async () => {
      useBTasksStore
        .getState()
        .setTasks(tasks.map((item) => ({ ...item, status: BTaskStatus.Completed })));
    });

    expect(getWorks).toHaveBeenCalledTimes(2);
  });

  it("does not refresh failed, cancelled, or unrelated completed tasks", async () => {
    await mountPage();

    await updateTask(task(`${DOWNLOAD_TASK_ID_PREFIX}RJ001`, BTaskStatus.Error));
    await updateTask(task(`${EXTRACT_TASK_ID_PREFIX}RJ002`, BTaskStatus.Cancelled));
    await updateTask(task("UnrelatedTask", BTaskStatus.Completed));

    expect(getWorks).toHaveBeenCalledTimes(1);
  });

  it("refreshes using the current search keyword, hidden filter, and page", async () => {
    await mountPage();

    await act(async () => {
      const input = screen.getByPlaceholderText("resourceSource.filter.keyword");

      fireEvent.change(input, { target: { value: "RJ123" } });
    });
    await act(async () => {
      fireEvent.keyDown(screen.getByPlaceholderText("resourceSource.filter.keyword"), {
        key: "Enter",
      });
    });
    await act(async () => {
      fireEvent.click(
        screen.getByRole("switch", { name: "resourceSource.dlsite.action.showHidden" }),
      );
    });
    await act(async () => {
      fireEvent.click(screen.getByLabelText("pagination item 2"));
    });
    getWorks.mockClear();

    await updateTask(task(`${EXTRACT_TASK_ID_PREFIX}RJ001`, BTaskStatus.Completed));

    expect(getWorks).toHaveBeenCalledTimes(1);
    expect(getWorks).toHaveBeenCalledWith({
      keyword: "RJ123",
      showHidden: true,
      pageIndex: 2,
      pageSize: 20,
    });
  });
});
