import { act, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import GeneralTaskDetailModal from "../components/GeneralTaskDetailModal";
import type { CenterTask } from "../downloadCenter";
const { get } = vi.hoisted(() => ({ get: vi.fn() }));
vi.mock("@/sdk/BApi", () => ({ default: { downloadCenter: { getDownloadCenterTask: get } } }));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: vi.fn() }),
}));
vi.mock("@/components/Workflow/WorkflowRunDetailModal", () => ({ default: () => null }));
vi.mock("../components/GeneralTaskRow", () => ({
  default: ({ task }: { task: CenterTask }) => (
    <div>
      {task.id} {task.status} {task.progress}%
    </div>
  ),
}));
vi.mock("@/components/bakaui", () => ({
  Modal: ({ children }: any) => <div>{children}</div>,
  Button: ({ children, onPress }: any) => <button onClick={onPress}>{children}</button>,
}));
const task = (id = "bt:1"): CenterTask => ({
  id,
  category: "general",
  provider: "bt",
  providerTaskId: id,
  name: id,
  outputDirectory: "/downloads",
  status: "running",
  progress: 10,
  createdAt: "2026-10-19",
  updatedAt: "2026-10-19",
  originKind: "manual",
  files: [],
  availableActions: ["stop"],
});
const props = { onAction: vi.fn(), onSource: vi.fn(), onFolder: vi.fn() };
beforeEach(() => {
  vi.useFakeTimers();
  get.mockReset();
});
afterEach(() => vi.useRealTimers());
describe("live general task details", () => {
  it("refreshes progress and actions while the modal is open", async () => {
    get.mockResolvedValueOnce({ code: 0, data: task() }).mockResolvedValue({
      code: 0,
      data: { ...task(), status: "completed", progress: 100, availableActions: ["delete"] },
    });
    render(<GeneralTaskDetailModal task={task()} {...props} />);
    await act(async () => {});
    expect(screen.getByText("bt:1 running 10%")).toBeInTheDocument();
    await act(async () => {
      await vi.advanceTimersByTimeAsync(3000);
    });
    expect(screen.getByText("bt:1 completed 100%")).toBeInTheDocument();
  });
  it("shows deletions and retains the last confirmed state on refresh errors", async () => {
    get.mockRejectedValueOnce(new Error("offline")).mockResolvedValue({ code: 404 });
    render(<GeneralTaskDetailModal task={task()} {...props} />);
    await act(async () => {});
    expect(screen.getByRole("alert")).toHaveTextContent("downloadCenter.error.load");
    await act(async () => {
      await vi.advanceTimersByTimeAsync(3000);
    });
    expect(screen.getByRole("status")).toHaveTextContent("downloadCenter.detail.deleted");
    expect(screen.queryByText("bt:1 running 10%")).toBeNull();
  });
  it("ignores an old task response after the detail target changes", async () => {
    let finish!: (value: unknown) => void;
    get
      .mockImplementationOnce(
        () =>
          new Promise((resolve) => {
            finish = resolve;
          }),
      )
      .mockResolvedValue({ code: 0, data: task("http:2") });
    const view = render(<GeneralTaskDetailModal task={task()} {...props} />);
    view.rerender(<GeneralTaskDetailModal task={task("http:2")} {...props} />);
    await act(async () => {});
    await act(async () => finish({ code: 0, data: { ...task(), progress: 99 } }));
    expect(screen.getByText("http:2 running 10%")).toBeInTheDocument();
    expect(screen.queryByText("bt:1 running 99%")).toBeNull();
    view.unmount();
    const count = get.mock.calls.length;
    await act(async () => {
      await vi.advanceTimersByTimeAsync(9000);
    });
    expect(get).toHaveBeenCalledTimes(count);
  });
});
