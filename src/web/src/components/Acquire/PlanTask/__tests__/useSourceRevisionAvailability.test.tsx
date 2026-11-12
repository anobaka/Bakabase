import { act, renderHook, waitFor } from "@testing-library/react";
import { beforeEach, expect, it, vi } from "vitest";
import { AcquisitionStatus } from "@/sdk/constants";
import type { PlanTask } from "../models";
import useSourceRevisionAvailability from "../useSourceRevisionAvailability";

const options = vi.hoisted(() => vi.fn());
vi.mock("../sourceRevisionApi", () => ({ sourceRevisionApi: { options } }));
const task = { id: 42, workflowRunId: 75, planRevision: 3, status: AcquisitionStatus.Waiting, updatedAt: "now" } as PlanTask;
const allowed = { taskId: 42, expectedRunId: 75, expectedRevision: 3, canRevise: true,
  candidates: [{ id: "plan-mirror" }] };
beforeEach(() => { vi.clearAllMocks(); options.mockResolvedValue(allowed); });

it("keeps the admitted action stable across detail refreshes without repeating source policy checks", async () => {
  const view = renderHook(({ value }) => useSourceRevisionAvailability(value), { initialProps: { value: task } });
  await waitFor(() => expect(view.result.current).toBe(true));
  view.rerender({ value: { ...task, updatedAt: "progress refreshed" } });
  expect(view.result.current).toBe(true);
  expect(options).toHaveBeenCalledTimes(1);
});

it("drops a stale response when the task's current execution changes", async () => {
  let resolve!: (value: typeof allowed) => void;
  options.mockReturnValueOnce(new Promise(done => { resolve = done; }));
  const view = renderHook(({ value }) => useSourceRevisionAvailability(value), { initialProps: { value: task } });
  view.rerender({ value: { ...task, workflowRunId: 76 } });
  await act(async () => { resolve(allowed); });
  expect(view.result.current).toBe(false);
  expect(options.mock.calls[0][1].aborted).toBe(true);
});

it.each([AcquisitionStatus.Pending, AcquisitionStatus.Running, AcquisitionStatus.Completed])("does not load source revisions for nonreplaceable status %s", status => {
  const { result } = renderHook(() => useSourceRevisionAvailability({ ...task, status }));
  expect(result.current).toBe(false);
  expect(options).not.toHaveBeenCalled();
});

it("hides the action on a failed read and never falls back to an earlier run's accepted result", async () => {
  const view = renderHook(({ value }) => useSourceRevisionAvailability(value), { initialProps: { value: task } });
  await waitFor(() => expect(view.result.current).toBe(true));
  options.mockRejectedValueOnce(new Error("offline"));
  view.rerender({ value: { ...task, planRevision: 4 } });
  await waitFor(() => expect(options).toHaveBeenCalledTimes(2));
  expect(view.result.current).toBe(false);
});
