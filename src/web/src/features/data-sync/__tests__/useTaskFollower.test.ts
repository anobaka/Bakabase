import { act, renderHook, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { UNSEEN_TASK_MS, useTaskFollower } from "../hooks/useTaskFollower";

import { bTask, minutesAgo } from "./dataSyncFixtures";

import { BTaskStatus } from "@/sdk/constants";
import { useBTasksStore } from "@/stores/bTasks";

// A task's completion and failure are followed through "Needs you" and the history's own tests.
describe("following data sync tasks started from here", () => {
  const follow = () => {
    const onOver = vi.fn(async () => undefined);
    const hook = renderHook(() => useTaskFollower<string>({ onOver, onFailed: vi.fn() }));

    return { follow: hook.result.current.follow, onOver };
  };

  it("ends every key of a task listed and then gone, once", async () => {
    useBTasksStore.setState({ tasks: [] });
    const { follow: start, onOver } = follow();

    act(() => start(["a", "b"], "DataSyncResolve"));
    act(() =>
      useBTasksStore.setState({
        tasks: [bTask("DataSyncResolve", BTaskStatus.Running, minutesAgo(0))],
      }),
    );
    act(() => useBTasksStore.setState({ tasks: [] }));

    await waitFor(() => expect(onOver).toHaveBeenCalledWith(["a", "b"]));
    expect(onOver).toHaveBeenCalledTimes(1);
  });

  it("gives up on a task never listed", async () => {
    vi.useFakeTimers({ toFake: ["setTimeout", "Date"] });
    try {
      useBTasksStore.setState({ tasks: [] });
      const { follow: start, onOver } = follow();

      act(() => start(["y"], "DataSyncResolve"));
      await act(async () => {
        vi.advanceTimersByTime(UNSEEN_TASK_MS);
      });
      expect(onOver).toHaveBeenCalledWith(["y"]);
    } finally {
      vi.useRealTimers();
    }
  });
});
