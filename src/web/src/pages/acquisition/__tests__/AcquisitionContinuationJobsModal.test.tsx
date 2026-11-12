import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import JobsModal, { AcquisitionContinuationStatus } from "../components/AcquisitionContinuationJobsModal";
const api = vi.hoisted(() => ({ list: vi.fn(), get: vi.fn(), cancel: vi.fn(), create: vi.fn() }));
vi.mock("../components/acquisitionContinuationApi", () => ({ acquisitionContinuationApi: api }));
vi.mock("@/components/bakaui", () => ({
  Modal: ({ visible, title, children, footer }: any) => visible ? <section role="dialog" aria-label={title}>{children}{footer}</section> : null,
  Button: ({ children, onPress, isLoading, isDisabled }: any) => <button disabled={isDisabled || isLoading} onClick={onPress}>{children}</button>,
  Chip: ({ children }: any) => <span>{children}</span>,
  Spinner: () => <span role="status">loading</span>,
  Pagination: ({ page, total, onChange }: any) => <button onClick={() => onChange(page + 1)}>page {page}/{total}</button>,
}));
const job = (status = "queued", rowStatus = "queued"): any => ({
  batchId: "job-1", scope: "workflow", status, createdAt: "2026-11-06T00:00:00Z",
  rows: [{ taskId: 1, title: "Resource 1", status: rowStatus }],
});
beforeEach(() => {
  for (const method of Object.values(api)) method.mockReset();
  api.list.mockResolvedValue([job()]); api.get.mockResolvedValue(job());
  api.cancel.mockResolvedValue(job("cancelled", "cancelled"));
});
afterEach(() => { cleanup(); vi.useRealTimers(); });

it("opens retained submission records without starting or cancelling tasks, and offers local queue navigation", async () => {
  const local = vi.fn();
  render(<JobsModal onShowLocalQueues={local} />);
  await screen.findByRole("button", { name: /acquisition.recovery.scope.workflow/ });
  fireEvent.click(screen.getByRole("button", { name: "acquisition.continue.localQueues" }));
  expect(local).toHaveBeenCalledTimes(1);
  fireEvent.click(screen.getByRole("button", { name: /acquisition.recovery.scope.workflow/ }));
  expect(screen.getByText(/Resource 1/)).toBeVisible();
  expect(screen.getByText("acquisition.continue.submissionHint")).toBeVisible();
  fireEvent.click(screen.getByRole("button", { name: "common.action.close" }));
  expect(screen.queryByRole("dialog")).toBeNull();
  expect(api.create).not.toHaveBeenCalled();
  expect(api.cancel).not.toHaveBeenCalled();
});

it("reports submitted work as submission rather than final resource completion and stops polling a settled record", async () => {
  vi.useFakeTimers();
  render(<AcquisitionContinuationStatus initialJob={job("finished", "submitted")} />);
  expect(screen.getAllByText(/acquisition.continue.status.submitted/).length).toBeGreaterThan(0);
  expect(screen.getByText("acquisition.continue.submissionHint")).toBeVisible();
  expect(screen.queryByText("acquisition.stage.completed")).toBeNull();
  await act(async () => { vi.advanceTimersByTime(12_000); });
  expect(api.get).not.toHaveBeenCalled();
  expect(screen.queryByRole("button", { name: "acquisition.continue.cancelRemaining" })).toBeNull();
});

it("does not let a late poll undo cancellation of unsubmitted work", async () => {
  vi.useFakeTimers();
  let finish!: (value: unknown) => void;
  api.get.mockReturnValueOnce(new Promise(resolve => { finish = resolve; }));
  const changed = vi.fn();
  render(<AcquisitionContinuationStatus initialJob={job()} onChanged={changed} />);
  await act(async () => { vi.advanceTimersByTime(4000); });
  expect(api.get).toHaveBeenCalledExactlyOnceWith("job-1");
  await act(async () => { fireEvent.click(screen.getByRole("button", { name: "acquisition.continue.cancelRemaining" })); });
  expect(api.cancel).toHaveBeenCalledExactlyOnceWith("job-1");
  await act(async () => finish(job()));
  expect(screen.getAllByText(/acquisition.continue.status.cancelled/).length).toBeGreaterThan(0);
  expect(screen.queryByRole("button", { name: "acquisition.continue.cancelRemaining" })).toBeNull();
  expect(changed).toHaveBeenCalledTimes(1);
});

it("ignores delayed status responses after leaving the view", async () => {
  vi.useFakeTimers();
  let finish!: (value: unknown) => void;
  api.get.mockReturnValueOnce(new Promise(resolve => { finish = resolve; }));
  const changed = vi.fn();
  const view = render(<AcquisitionContinuationStatus initialJob={job()} onChanged={changed} />);
  await act(async () => { vi.advanceTimersByTime(4000); });
  view.unmount();
  await act(async () => finish(job("finished", "submitted")));
  expect(changed).not.toHaveBeenCalled();
  expect(api.cancel).not.toHaveBeenCalled();
});

it("shows failed history reads without treating them as an empty successful queue", async () => {
  api.list.mockRejectedValueOnce(new Error("offline"));
  render(<JobsModal />);
  await screen.findByRole("alert");
  expect(screen.queryByText("acquisition.continue.noRecords")).toBeNull();
  fireEvent.click(screen.getByRole("button", { name: "common.action.refresh" }));
  await waitFor(() => expect(api.list).toHaveBeenCalledTimes(2));
  await screen.findByRole("button", { name: /acquisition.recovery.scope.workflow/ });
  expect(api.create).not.toHaveBeenCalled();
});

it("opens the exact existing continuation from a task without creating another request", async () => {
  const back = vi.fn();
  render(<JobsModal initialBatchId="job-1" onBackToSelection={back} />);
  await screen.findByText(/Resource 1/);
  expect(api.get).toHaveBeenCalledExactlyOnceWith("job-1");
  expect(api.list).not.toHaveBeenCalled();
  expect(api.create).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("button", { name: "acquisition.continueActions.back" }));
  expect(back).toHaveBeenCalledTimes(1);
});
