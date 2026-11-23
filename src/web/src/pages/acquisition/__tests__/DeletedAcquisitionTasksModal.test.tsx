import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import DeletedAcquisitionTasksModal from "../components/DeletedAcquisitionTasksModal";
import DeleteAcquisitionTasksModal from "../components/DeleteAcquisitionTasksModal";

const api = vi.hoisted(() => ({ history: vi.fn(), portal: vi.fn() }));
vi.mock("@/sdk/BApi", () => ({ default: { acquisition: { getDeletedAcquisitionTasks: api.history } } }));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({ useBakabaseContext: () => ({ createPortal: api.portal }) }));
vi.mock("../components/DeleteAcquisitionTasksModal", () => ({ default: () => null }));
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string, values?: Record<string, string>) => values?.reasons ?? (values?.intermediate ? `${values.intermediate} / ${values.delivery}` : key) }) }));
vi.mock("@/components/bakaui", () => ({
  Modal: ({ visible, children }: any) => visible && <div>{children}</div>,
  Button: ({ children, onPress, isDisabled }: any) => <button disabled={isDisabled} onClick={onPress}>{children}</button>,
  Checkbox: ({ children, isSelected, onValueChange, "aria-label": label }: any) => <label><input aria-label={label} type="checkbox" checked={isSelected} onChange={e => onValueChange(e.target.checked)} />{children}</label>,
  Pagination: ({ onChange }: any) => <button onClick={() => onChange(2)}>page two</button>,
  Spinner: () => <span role="status" />,
}));
const row = (taskId: number, intermediateCleanup: string, deliveryCleanup: string) => ({taskId, title: `Task ${taskId}`, deletedAt: "2026-11-20T18:00:00Z", intermediateCleanup, deliveryCleanup, lastReasonCodes: []});

describe("deleted acquisition tasks", () => {
  beforeEach(() => vi.clearAllMocks());
  afterEach(() => vi.useRealTimers());

  it("opens a tombstone and offers scoped cleanup without disk previews or automatic deletion", async () => {
    api.history.mockResolvedValue({code: 0, data: {items: [row(9, "incomplete", "completed"), row(7, "unknown", "unknown")], totalCount: 2}});
    const open = vi.fn();
    render(<DeletedAcquisitionTasksModal onOpenTask={open} />);
    fireEvent.click(await screen.findByText("#9 Task 9"));
    expect(open).toHaveBeenCalledWith(9);
    expect(screen.getByText("acquisition.delete.history.scope.unknown / acquisition.delete.history.scope.unknown")).toBeInTheDocument();
    fireEvent.click(screen.getByText("acquisition.delete.history.selectPage"));
    fireEvent.click(screen.getByText("acquisition.delete.retained.title"));
    expect(api.portal).toHaveBeenCalledWith(DeleteAcquisitionTasksModal, expect.objectContaining({
      taskIds: [9, 7], retryRetainedFiles: true, initialScopes: { intermediate: false, delivery: false },
    }));
    fireEvent.click(screen.getByText("acquisition.delete.history.filter.unknown"));
    await waitFor(() => expect(api.history).toHaveBeenLastCalledWith({pageIndex: 1,pageSize: 25,filter: "unknown"},expect.anything()));
    expect(screen.getByText("acquisition.delete.retained.title")).toBeDisabled();
  });

  it("keeps legacy SMB reason text without assuming which scope is automatically waiting", async () => {
    api.history.mockResolvedValue({code: 0, data: {items: [{...row(10, "incomplete", "completed"), lastReasonCodes: ["CLEANUP_SMB_AUTO_PENDING"]}], totalCount: 1}});
    render(<DeletedAcquisitionTasksModal onOpenTask={vi.fn()} />);
    expect(await screen.findByText("acquisition.delete.reason.CLEANUP_SMB_AUTO_PENDING")).toBeInTheDocument();
    fireEvent.click(screen.getByText("acquisition.delete.history.review"));
    expect(api.portal).toHaveBeenCalledWith(DeleteAcquisitionTasksModal, expect.objectContaining({
      taskIds: [10], initialScopes: {intermediate: true, delivery: false},
    }));
  });

  it("preselects only the previously incomplete delivery scope when reopening one task", async () => {
    api.history.mockResolvedValue({code: 0, data: {items: [row(10, "completed", "incomplete")], totalCount: 1}});
    render(<DeletedAcquisitionTasksModal onOpenTask={vi.fn()} />);
    fireEvent.click(await screen.findByText("acquisition.delete.history.review"));
    expect(api.portal).toHaveBeenCalledWith(DeleteAcquisitionTasksModal, expect.objectContaining({
      taskIds: [10], retryRetainedFiles: true, initialScopes: {intermediate: false, delivery: true},
    }));
  });

  it("displays only confirmed waiting scopes and keeps manual review available for the other scope", async () => {
    const mixed = {...row(15, "incomplete", "incomplete"), awaitingFileReleaseScopes: ["delivery"],
      lastReasonCodes: ["CLEANUP_SMB_AUTO_PENDING", "CLEANUP_FILES_RETAINED"]};
    api.history.mockResolvedValue({code: 0, data: {items: [mixed], totalCount: 1}});
    render(<DeletedAcquisitionTasksModal onOpenTask={vi.fn()} />);
    expect(await screen.findByText("acquisition.delete.history.scope.incomplete / acquisition.delete.history.scope.awaitingFileRelease")).toBeInTheDocument();
    expect(screen.getByText("acquisition.delete.history.automaticCleanup")).toBeInTheDocument();
    expect(screen.getByText("acquisition.delete.reason.CLEANUP_FILES_RETAINED")).toBeInTheDocument();
    expect(screen.queryByText("acquisition.delete.reason.CLEANUP_SMB_AUTO_PENDING")).not.toBeInTheDocument();
    fireEvent.click(screen.getByText("acquisition.delete.history.review"));
    expect(api.portal).toHaveBeenCalledWith(DeleteAcquisitionTasksModal, expect.objectContaining({
      taskIds: [15], initialScopes: { intermediate: true, delivery: false },
    }));
  });

  it("flags only selections containing waiting workflow executions for a server-backed review", async () => {
    const waiting = {...row(10, "incomplete", "completed"), waitingRunIds: [90,91]};
    api.history.mockResolvedValue({code: 0, data: {items: [waiting, row(11, "incomplete", "completed")], totalCount: 2}});
    render(<DeletedAcquisitionTasksModal onOpenTask={vi.fn()} />);
    await screen.findByText("acquisition.delete.history.waitingRuns");
    const buttons = screen.getAllByText("acquisition.delete.history.review");
    fireEvent.click(buttons[1]);
    expect(api.portal).toHaveBeenLastCalledWith(DeleteAcquisitionTasksModal, expect.objectContaining({taskIds:[11], reviewWaitingRuns:false}));
    fireEvent.click(screen.getByText("acquisition.delete.history.selectPage"));
    fireEvent.click(screen.getByText("acquisition.delete.retained.title"));
    expect(api.portal).toHaveBeenLastCalledWith(DeleteAcquisitionTasksModal, expect.objectContaining({taskIds:[10,11], reviewWaitingRuns:true}));
  });

  it("refreshes waiting scopes without blanking the page or losing selections, then stops after release", async () => {
    vi.useFakeTimers();
    const pending = {...row(15, "incomplete", "completed"), awaitingFileReleaseScopes: ["intermediate"]};
    const stable = row(16, "unknown", "unknown");
    const done = row(15, "completed", "completed");
    const page = (items: Array<ReturnType<typeof row>>) => ({code: 0, data: {items, totalCount: items.length}});
    let finishRefresh!: (v: ReturnType<typeof page>) => void;
    api.history.mockResolvedValueOnce(page([pending, stable]))
      .mockRejectedValueOnce(new Error("temporarily offline"))
      .mockImplementationOnce(() => new Promise(resolve => { finishRefresh = resolve; }));
    await act(async () => { render(<DeletedAcquisitionTasksModal onOpenTask={vi.fn()} />); });
    expect(screen.getByText("acquisition.delete.history.scope.awaitingFileRelease / acquisition.delete.history.scope.completed")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("checkbox", {name: "#15"}));
    fireEvent.click(screen.getByRole("checkbox", {name: "#16"}));
    await act(async () => { await vi.advanceTimersByTimeAsync(15000); });
    expect(screen.getByRole("alert")).toBeInTheDocument();
    expect(screen.getByRole("checkbox", {name: "#15"})).toBeChecked();
    await act(async () => { await vi.advanceTimersByTimeAsync(15000); });
    expect(screen.queryByRole("status")).not.toBeInTheDocument();
    expect(screen.getByText("#15 Task 15")).toBeInTheDocument();
    await act(async () => { finishRefresh(page([done])); });
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    expect(screen.getByRole("checkbox", {name: "#15"})).toBeChecked();
    expect(screen.queryByRole("checkbox", {name: "#16"})).not.toBeInTheDocument();
    expect(screen.queryByText("acquisition.delete.history.automaticCleanup")).not.toBeInTheDocument();
    expect(screen.getByText("acquisition.delete.history.scope.completed / acquisition.delete.history.scope.completed")).toBeInTheDocument();
    fireEvent.click(screen.getByText("acquisition.delete.retained.title"));
    expect(api.portal).toHaveBeenCalledWith(DeleteAcquisitionTasksModal, expect.objectContaining({taskIds: [15]}));
    await act(async () => { await vi.advanceTimersByTimeAsync(60000); });
    expect(api.history).toHaveBeenCalledTimes(3);
  });

  it("refreshes the selected filter and page, returning to the last available page after release", async () => {
    vi.useFakeTimers();
    const pending = (id: number) => ({...row(id, "incomplete", "completed"), awaitingFileReleaseScopes: ["intermediate"]});
    const page = (items: Array<ReturnType<typeof row>>, totalCount: number) => ({code: 0, data: {items, totalCount}});
    api.history.mockResolvedValueOnce(page([row(1, "completed", "completed")], 1))
      .mockResolvedValueOnce(page([pending(40)], 26)).mockResolvedValueOnce(page([pending(9)], 26))
      .mockResolvedValueOnce(page([], 1)).mockResolvedValueOnce(page([pending(40)], 1));
    await act(async () => { render(<DeletedAcquisitionTasksModal onOpenTask={vi.fn()} />); });
    await act(async () => { fireEvent.click(screen.getByText("acquisition.delete.history.filter.awaitingFileRelease")); });
    await act(async () => { fireEvent.click(screen.getByText("page two")); });
    expect(screen.getByText("#9 Task 9")).toBeInTheDocument();
    await act(async () => { await vi.advanceTimersByTimeAsync(15000); });
    expect(screen.queryByText("#9 Task 9")).not.toBeInTheDocument();
    expect(screen.getByText("#40 Task 40")).toBeInTheDocument();
    expect(api.history.mock.calls.slice(1).map(([query]) => [query.filter, query.pageIndex])).toEqual([
      ["awaitingFileRelease", 1], ["awaitingFileRelease", 2], ["awaitingFileRelease", 2], ["awaitingFileRelease", 1],
    ]);
  });
});
