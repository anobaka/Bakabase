import { act, renderHook, waitFor } from "@testing-library/react";
import { beforeEach, expect, it, vi } from "vitest";
import useSourceCorrections from "../useSourceCorrections";
import type { PlanTask } from "../models";

const api = vi.hoisted(() => ({ sourceCorrections: vi.fn() }));
vi.mock("../api", () => ({ planTaskApi: api }));
const task = { id: 90, workflowRunId: 2732, planRevision: 1, status: 3 } as PlanTask;
const current = {
  taskId: 90,
  expectedRunId: 2732,
  expectedRevision: 1,
  canCorrect: true,
  sources: [],
};
beforeEach(() => api.sourceCorrections.mockReset().mockResolvedValue(current));

it("does not query nonexecuted drafts or reload for ordinary progress renders", async () => {
  const view = renderHook(({ task }) => useSourceCorrections(90, task), {
    initialProps: { task: undefined as PlanTask | undefined },
  });
  expect(api.sourceCorrections).not.toHaveBeenCalled();
  view.rerender({ task });
  await waitFor(() => expect(view.result.current.data).toEqual(current));
  view.rerender({ task: { ...task, updatedAt: "later" } });
  expect(api.sourceCorrections).toHaveBeenCalledTimes(1);
});

it("discards old-run data immediately and rejects its late response", async () => {
  let resolve!: (value: unknown) => void;
  const pending = new Promise((done) => {
    resolve = done;
  });
  api.sourceCorrections
    .mockReturnValueOnce(pending)
    .mockResolvedValue({ ...current, expectedRunId: 2733, expectedRevision: 2 });
  const view = renderHook(({ task }) => useSourceCorrections(90, task), { initialProps: { task } });
  const oldSignal = api.sourceCorrections.mock.calls[0][1] as AbortSignal;
  view.rerender({ task: { ...task, workflowRunId: 2733, planRevision: 2 } });
  expect(oldSignal.aborted).toBe(true);
  await waitFor(() => expect(view.result.current.data?.expectedRunId).toBe(2733));
  await act(async () => resolve(current));
  expect(view.result.current.data?.expectedRunId).toBe(2733);
});

it.each([{ taskId: 91 }, { expectedRunId: 2733 }, { expectedRevision: 2 }])(
  "does not expose correction capabilities for a mismatched task snapshot %s",
  async (changes) => {
    api.sourceCorrections.mockResolvedValue({ ...current, ...changes });
    const view = renderHook(() => useSourceCorrections(90, task));
    await waitFor(() => expect(view.result.current.failed).toBe(true));
    expect(view.result.current.data).toBeUndefined();
  },
);
