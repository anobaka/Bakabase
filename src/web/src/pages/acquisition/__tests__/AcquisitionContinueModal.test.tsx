import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import AcquisitionContinueModal from "../components/AcquisitionContinueModal";

const panes = vi.hoisted(() => ({
  local: vi.fn(),
  recovery: vi.fn(),
  history: vi.fn(),
}));
vi.mock("../components/AcquisitionLocalBatchModal", () => ({
  default: (props: any) => {
    panes.local(props);
    return (
      <section role="dialog" aria-label={props.automatic ? "automatic" : "local"}>
        {props.onShowDownloads && <button onClick={props.onShowDownloads}>downloads</button>}
        {props.onOpenQueue && (
          <button
            onClick={() => {
              props.onSelectionChange({ 1: "continue" });
              props.onOpenQueue({ kind: "localQueue", batchId: "queue-fixed" });
            }}
          >
            inspect
          </button>
        )}
        {props.onBackToSelection && <button onClick={props.onBackToSelection}>back</button>}
        {props.onInspectPausedOriginal && (
          <button onClick={() => {
            props.onSelectionChange({ 1: "continue" });
            props.onInspectPausedOriginal(3);
          }}>check separately</button>
        )}
        {props.onShowContinuationHistory && (
          <button onClick={props.onShowContinuationHistory}>history</button>
        )}
        <button onClick={props.onDestroyed}>close</button>
      </section>
    );
  },
}));
vi.mock("../components/AcquisitionRecoveryModal", () => ({
  default: (props: any) => {
    panes.recovery(props);
    return (
      <section role="dialog" aria-label={props.initialScope}>
        <button onClick={() => props.onContinuationScopeChange("localQueue")}>back</button>
        <button onClick={props.onDestroyed}>close</button>
      </section>
    );
  },
}));
vi.mock("../components/AcquisitionContinuationJobsModal", () => ({
  default: (props: any) => {
    panes.history(props);
    return (
      <section role="dialog" aria-label="history">
        <button onClick={props.onShowLocalQueues}>local</button>
        <button onClick={props.onDestroyed}>close</button>
      </section>
    );
  },
}));
beforeEach(() => vi.clearAllMocks());
afterEach(cleanup);

it("opens automatic task actions and requires an explicit switch to download-only recovery", () => {
  const destroyed = vi.fn();
  const view = render(
    <AcquisitionContinueModal
      taskIds={[3, 1, 3, -1]}
      scopeLabel="Selected tasks"
      onDestroyed={destroyed}
    />,
  );
  expect(screen.getAllByRole("dialog")).toHaveLength(1);
  const original = panes.local.mock.calls.at(-1)![0];
  expect(original.taskIds).toEqual([3, 1]);
  expect(original.automatic).toBe(true);
  view.rerender(<AcquisitionContinueModal taskIds={[3, 1, 99]} onDestroyed={destroyed} />);
  fireEvent.click(screen.getByRole("button", { name: "downloads" }));
  expect(screen.getAllByRole("dialog")).toHaveLength(1);
  expect(panes.recovery.mock.calls.at(-1)![0]).toEqual(
    expect.objectContaining({
      taskIds: [3, 1],
      initialScope: "downloads",
      fixedScope: true,
    }),
  );
  original.onDestroyed();
  expect(destroyed).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("button", { name: "back" }));
  expect(screen.getByRole("dialog")).toHaveAttribute("aria-label", "automatic");
  original.onDestroyed();
  expect(destroyed).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("button", { name: "close" }));
  expect(destroyed).toHaveBeenCalledTimes(1);
});

it("preserves action and task choices while opening the exact existing queue in one modal", () => {
  render(<AcquisitionContinueModal taskIds={[1, 3]} />);
  fireEvent.click(screen.getByRole("button", { name: "inspect" }));
  expect(screen.getAllByRole("dialog")).toHaveLength(1);
  expect(panes.local.mock.calls.at(-1)![0].initialBatchId).toBe("queue-fixed");
  fireEvent.click(screen.getByRole("button", { name: "back" }));
  expect(panes.local.mock.calls.at(-1)![0].initialSelection).toEqual({
    1: "continue",
  });
});

it("checks the timed-out paused task separately and preserves the other batch choices", () => {
  render(<AcquisitionContinueModal taskIds={[1, 3]} />);
  fireEvent.click(screen.getByRole("button", { name: "check separately" }));
  expect(screen.getAllByRole("dialog")).toHaveLength(1);
  expect(panes.local.mock.calls.at(-1)![0].taskIds).toEqual([3]);
  expect(panes.local.mock.calls.at(-1)![0].onInspectPausedOriginal).toBeUndefined();
  fireEvent.click(screen.getByRole("button", { name: "back" }));
  expect(panes.local.mock.calls.at(-1)![0].taskIds).toEqual([1, 3]);
  expect(panes.local.mock.calls.at(-1)![0].initialSelection).toEqual({ 1: "continue" });
});

it("opens local queue history and can inspect continuation records without task selection", () => {
  render(<AcquisitionContinueModal />);
  expect(screen.getByRole("dialog")).toHaveAttribute("aria-label", "local");
  expect(panes.local.mock.calls.at(-1)![0].taskIds).toBeUndefined();
  fireEvent.click(screen.getByRole("button", { name: "history" }));
  expect(screen.getAllByRole("dialog")).toHaveLength(1);
  expect(screen.getByRole("dialog")).toHaveAttribute("aria-label", "history");
  expect(panes.recovery).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("button", { name: "local" }));
  expect(screen.getByRole("dialog")).toHaveAttribute("aria-label", "local");
});

it("keeps filtered AI candidates in the same per-task selection dialog", () => {
  render(<AcquisitionContinueModal taskIds={[2, 5]} filteredAnalysis />);
  expect(screen.getAllByRole("dialog")).toHaveLength(1);
  expect(screen.getByRole("dialog", { name: "automatic" })).toBeVisible();
  expect(panes.local.mock.calls.at(-1)![0].taskIds).toEqual([2, 5]);
});
