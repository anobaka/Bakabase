import { act, renderHook, waitFor } from "@testing-library/react";
import { beforeEach, expect, it, vi } from "vitest";
import BApi from "@/sdk/BApi";
import { useSourceAcquisitions } from "../useSourceAcquisitions";
import { sourceRefKey } from "../sourceHistory";

vi.mock("@/sdk/BApi", () => ({default: {acquisition: {getAcquisitionPlansBySources: vi.fn()}}}));
const source = {module: "producer", entityKind: "item", entityId: "4"};
const response = (id: number) => ({code: 0, data: [{source, tasks: [{id, status: 3, phase: "ready", builderId: "prepared", workflowRunId: null}]}]});
beforeEach(() => vi.mocked(BApi.acquisition.getAcquisitionPlansBySources).mockReset());

it("shows a prepared task before any workflow exists and keeps it during refresh", async () => {
  let finish!: (value: any) => void;
  vi.mocked(BApi.acquisition.getAcquisitionPlansBySources)
    .mockResolvedValueOnce(response(41) as any)
    .mockImplementationOnce(() => new Promise(resolve => {finish = resolve;}));
  const {result} = renderHook(() => useSourceAcquisitions([source]));
  await waitFor(() => expect(result.current.rows.get(sourceRefKey(source))?.[0].id).toBe(41));
  expect(result.current.rows.get(sourceRefKey(source))?.[0].workflowRunId).toBeNull();
  act(() => result.current.refresh());
  expect(result.current.rows.get(sourceRefKey(source))?.[0].id).toBe(41);
  await act(async () => finish(response(42)));
  expect(result.current.rows.get(sourceRefKey(source))?.[0].id).toBe(42);
});

it("does not replace a new scope with a late response from the old source", async () => {
  let finish!: (value: any) => void;
  const next = {...source, entityId: "5"};
  vi.mocked(BApi.acquisition.getAcquisitionPlansBySources)
    .mockImplementationOnce(() => new Promise(resolve => {finish = resolve;}))
    .mockResolvedValueOnce({code: 0, data: [{source: next, tasks: []}]});
  const {result, rerender} = renderHook(({sources}) => useSourceAcquisitions(sources), {initialProps: {sources: [source]}});
  rerender({sources: [next]});
  await waitFor(() => expect(result.current.rows.has(sourceRefKey(next))).toBe(true));
  await act(async () => finish(response(41)));
  expect(result.current.rows.has(sourceRefKey(source))).toBe(false);
});
