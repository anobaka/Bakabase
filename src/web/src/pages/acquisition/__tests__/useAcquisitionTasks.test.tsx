import { act, cleanup, renderHook, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import useAcquisitionTasks from "../useAcquisitionTasks";

const search = vi.hoisted(() => vi.fn());
vi.mock("@/sdk/BApi", () => ({ default: { acquisition: { searchAcquisitionTasks: search } } }));
const page = (ids = [12]) => ({ code: 0, data: {
  items: [], filteredTaskIds: ids, checkableTaskIds: ids, totalCount: ids.length,
  attentionCounts: { localStorage: 8 }, factCounts: { downloadedPending: 12, spaceInsufficient: 8 },
  taskFacts: {}, stageCounts: { all: 20 },
} });
beforeEach(() => { search.mockReset().mockResolvedValue(page()); });
afterEach(cleanup);

it("does not abort or re-request equivalent AND selections in a different order", async () => {
  const query = { stage: "attention", page: 1, pageSize: 24,
    attentionTags: ["networkIssue", "localStorage"], facts: ["spaceInsufficient", "downloadedPending"] };
  const { result, rerender } = renderHook((props) => useAcquisitionTasks(props), { initialProps: query });
  await waitFor(() => expect(result.current.data?.totalCount).toBe(1));
  const options = search.mock.calls[0][1];
  rerender({ ...query, attentionTags: ["localStorage", "networkIssue", "localStorage"],
    facts: ["downloadedPending", "spaceInsufficient"] });
  expect(search).toHaveBeenCalledTimes(1);
  expect(options.signal.aborted).toBe(false);
  expect(search.mock.calls[0][0]).toEqual({ ...query, attentionTags: ["localStorage", "networkIssue"],
    facts: ["downloadedPending", "spaceInsufficient"] });
});

it("aborts an obsolete intersection, retains base facet counts and never exposes its batch IDs", async () => {
  const initial = { keyword: "resource", stage: "attention", page: 1, pageSize: 24, facts: [] as string[] };
  const { result, rerender } = renderHook((query) => useAcquisitionTasks(query), { initialProps: initial });
  await waitFor(() => expect(result.current.data?.filteredTaskIds).toEqual([12]));
  let finishOld!: (value: unknown) => void;
  search.mockReturnValueOnce(new Promise(resolve => { finishOld = resolve; }));
  rerender({ ...initial, facts: ["downloadedPending"] });
  await waitFor(() => expect(search).toHaveBeenCalledTimes(2));
  const obsolete = search.mock.calls[1][1].signal;
  expect(result.current.data).toBeUndefined();
  expect(result.current.factCounts?.downloadedPending).toBe(12);
  search.mockResolvedValueOnce(page([2, 9]));
  rerender({ ...initial, facts: ["spaceInsufficient", "downloadedPending"] });
  await waitFor(() => expect(result.current.data?.filteredTaskIds).toEqual([2, 9]));
  expect(obsolete.aborted).toBe(true);
  await act(async () => finishOld(page([999])));
  expect(result.current.data?.filteredTaskIds).toEqual([2, 9]);
});

it("does not present another keyword or stage's facet counts as current", async () => {
  const { result, rerender } = renderHook((query) => useAcquisitionTasks(query), {
    initialProps: { keyword: "first", stage: "attention", page: 1, pageSize: 24 },
  });
  await waitFor(() => expect(result.current.factCounts?.spaceInsufficient).toBe(8));
  search.mockReturnValueOnce(new Promise(() => {}));
  rerender({ keyword: "second", stage: "attention", page: 1, pageSize: 24 });
  expect(result.current.data).toBeUndefined();
  expect(result.current.attentionCounts).toBeUndefined();
  expect(result.current.factCounts).toBeUndefined();
});

it("invalidates source-scoped facet counts and stale batch IDs when the download source changes", async () => {
  const initial = { stage: "attention", keyword: "archive", downloadSource: "http", page: 1, pageSize: 24 };
  const { result, rerender } = renderHook(query => useAcquisitionTasks(query), { initialProps: initial });
  await waitFor(() => expect(result.current.data?.filteredTaskIds).toEqual([12]));
  let completeOld!: (value: unknown) => void;
  search.mockReturnValueOnce(new Promise(resolve => { completeOld = resolve; }));
  rerender({ ...initial, downloadSource: "bt" });
  await waitFor(() => expect(search).toHaveBeenCalledTimes(2));
  const oldSignal = search.mock.lastCall![1].signal;
  expect(result.current.data).toBeUndefined();
  expect(result.current.attentionCounts).toBeUndefined();
  expect(result.current.factCounts).toBeUndefined();
  search.mockResolvedValueOnce(page([6]));
  rerender({ ...initial, downloadSource: "site:14" });
  await waitFor(() => expect(result.current.data?.filteredTaskIds).toEqual([6]));
  expect(oldSignal.aborted).toBe(true);
  await act(async () => completeOld(page([999])));
  expect(result.current.data?.filteredTaskIds).toEqual([6]);
  expect(search).toHaveBeenLastCalledWith(expect.objectContaining({ downloadSource: "site:14" }), expect.anything());
});
