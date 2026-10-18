import type { MoveBatch } from "../types";

import React from "react";
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const api = vi.hoisted(() => ({ context: vi.fn(), batches: vi.fn() }));

vi.mock("../api", () => ({ movePanelApi: api }));
vi.mock("@/config/env", () => ({ default: { apiEndpoint: "/api" } }));
vi.mock("@/components/bakaui", async () => {
  const { Button, Checkbox, Progress, Select, SelectItem, Tooltip } = await import("@heroui/react");

  return {
    Button,
    Checkbox,
    Progress,
    Tooltip,
    Modal: () => null,
    Select: ({ dataSource, ...props }: any) => (
      <Select {...props}>
        {(dataSource ?? []).map((item: any) => (
          <SelectItem
            key={item.value}
            aria-label={item.label}
            description={item.description}
            textValue={item.label}
          >
            {item.label}
          </SelectItem>
        ))}
      </Select>
    ),
  };
});
import TaskList from "../TaskList";
import { readClearedMoveTaskIds } from "../taskHistory";

import { useResourceMovePanelStore } from "@/stores/resourceMovePanel";

const initial = useResourceMovePanelStore.getState();
const sourceContext = { nodeId: "this-server", libraryEpoch: "this-library" };
const taskBatch = (batchId: string, status: MoveBatch["status"], sourceTabId = "a"): MoveBatch => ({
  batchId,
  sourceTabId,
  destDir: `/target/${batchId}`,
  status,
  counts: {
    total: 1,
    succeeded: status === "completed" ? 1 : 0,
    failed: 0,
    cancelled: 0,
    skipped: 0,
    waiting: 0,
  },
  records: [],
});

beforeEach(() => {
  localStorage.clear();
  vi.clearAllMocks();
  api.context.mockReset().mockResolvedValue(sourceContext);
  api.batches.mockReset().mockResolvedValue([]);
  useResourceMovePanelStore.setState(
    { ...initial, sourceContext, context: { tabId: "a", resources: [], selectedResources: [] } },
    true,
  );
});
afterEach(cleanup);

describe("move task list controls", () => {
  it("explains source-tab filtering and restores all tasks outside resource tabs", async () => {
    useResourceMovePanelStore.setState({
      batches: [taskBatch("this-tab", "running"), taskBatch("other-tab", "waiting", "b")],
    });
    render(<TaskList />);
    fireEvent.click(screen.getByRole("button", { name: /Filter move tasks by source tab/ }));
    await waitFor(() =>
      expect(screen.getByText("Show move tasks started from every resource tab.")).toBeVisible(),
    );
    expect(
      screen.getByText("Show only move tasks started from the current resource tab."),
    ).toBeVisible();
    fireEvent.click(screen.getByRole("option", { name: "This tab's tasks" }));
    expect(screen.getByTestId("move-task-this-tab")).toBeVisible();
    expect(screen.queryByTestId("move-task-other-tab")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Other tabs need attention: 1" })).toBeVisible();

    act(() => {
      useResourceMovePanelStore.setState({ context: { resources: [], selectedResources: [] } });
    });
    expect(screen.getByTestId("move-task-other-tab")).toBeVisible();
    expect(
      screen.getByRole("button", { name: /Filter move tasks by source tab/ }),
    ).toHaveTextContent("All tabs' tasks");
  });

  it("clears completed tasks from every history page and source tab while retaining all other states", async () => {
    const firstPage = [
      taskBatch("done-here", "completed"),
      taskBatch("done-elsewhere", "completed", "b"),
      ...Array.from({ length: 98 }, (_, index) => taskBatch(`running-${index}`, "running")),
    ];
    const olderPage = [
      taskBatch("done-older", "completed", "b"),
      ...(
        [
          "queued",
          "stopping",
          "waiting",
          "partial",
          "failed",
          "cancelled",
          "needsRecovery",
        ] as const
      ).map((status) => taskBatch(status, status)),
    ];

    api.batches.mockImplementation((skip = 0) =>
      Promise.resolve(skip === 0 ? firstPage : olderPage),
    );
    useResourceMovePanelStore.setState({ batches: firstPage });
    const view = render(<TaskList />);

    fireEvent.click(screen.getByRole("button", { name: /Filter move tasks by source tab/ }));
    fireEvent.click(await screen.findByRole("option", { name: "This tab's tasks" }));
    fireEvent.click(screen.getByRole("button", { name: "Clear completed tasks" }));

    await waitFor(() =>
      expect(screen.queryByTestId("move-task-done-here")).not.toBeInTheDocument(),
    );
    expect(api.batches).toHaveBeenCalledWith(0);
    expect(api.batches).toHaveBeenCalledWith(100);
    expect(readClearedMoveTaskIds(sourceContext)).toEqual(
      new Set(["done-here", "done-elsewhere", "done-older"]),
    );

    fireEvent.click(screen.getByRole("button", { name: "Load older tasks" }));
    await screen.findByTestId("move-task-needsRecovery");
    for (const status of [
      "queued",
      "stopping",
      "waiting",
      "partial",
      "failed",
      "cancelled",
      "needsRecovery",
    ]) {
      expect(screen.getByTestId(`move-task-${status}`)).toBeVisible();
    }
    expect(screen.queryByTestId("move-task-done-older")).not.toBeInTheDocument();
    view.unmount();
    render(<TaskList />);
    expect(screen.queryByTestId("move-task-done-here")).not.toBeInTheDocument();
    expect(screen.queryByTestId("move-task-done-elsewhere")).not.toBeInTheDocument();
    expect(screen.getByTestId("move-task-running-0")).toBeVisible();
  });

  it("keeps completed tasks visible if scanning history fails", async () => {
    useResourceMovePanelStore.setState({ batches: [taskBatch("keep-done", "completed")] });
    api.batches.mockRejectedValue(new Error("History unavailable"));
    render(<TaskList />);
    fireEvent.click(screen.getByRole("button", { name: "Clear completed tasks" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("History unavailable");
    expect(screen.getByTestId("move-task-keep-done")).toBeVisible();
    expect(readClearedMoveTaskIds(sourceContext).size).toBe(0);
  });

  it("does not persist a stale cleanup after the source library changes", async () => {
    let finishHistory!: (value: MoveBatch[]) => void;

    api.batches.mockReturnValue(
      new Promise<MoveBatch[]>((resolve) => {
        finishHistory = resolve;
      }),
    );
    useResourceMovePanelStore.setState({ batches: [taskBatch("same-id", "completed")] });
    render(<TaskList />);
    fireEvent.click(screen.getByRole("button", { name: "Clear completed tasks" }));
    await waitFor(() => expect(api.batches).toHaveBeenCalledWith(0));
    const differentSource = { ...sourceContext, libraryEpoch: "replacement-library" };

    act(() => useResourceMovePanelStore.setState({ sourceContext: differentSource }));
    await act(async () => finishHistory([taskBatch("same-id", "completed")]));
    expect(screen.getByTestId("move-task-same-id")).toBeVisible();
    expect(readClearedMoveTaskIds(sourceContext).size).toBe(0);
    expect(readClearedMoveTaskIds(differentSource).size).toBe(0);
  });
});
