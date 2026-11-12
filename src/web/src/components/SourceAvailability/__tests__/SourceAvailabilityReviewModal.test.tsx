import { StrictMode, type ReactNode } from "react";
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import SourceAvailabilityReviewModal from "../SourceAvailabilityReviewModal";
import type { SourceAvailabilityPreview, SourceAvailabilityRow } from "../models";

const api = vi.hoisted(() => ({ preview: vi.fn(), batch: vi.fn(), apply: vi.fn() }));
vi.mock("../api", () => ({ sourceAvailabilityApi: api }));
vi.mock("../SourceHealthBadge", () => ({
  default: ({ health }: { health?: { status: string } }) => <span>{health?.status}</span>,
}));
vi.mock("@/components/Acquire/AcquisitionSourceDetails", () => ({
  default: ({ source }: { source: SourceAvailabilityRow }) => (
    <div>
      {source.url}
      <span>{source.accessCode}</span>
    </div>
  ),
}));
vi.mock("@/components/bakaui", () => ({
  Modal: ({
    children,
    footer,
    visible,
    title,
  }: {
    children: ReactNode;
    footer: ReactNode;
    visible: boolean;
    title: string;
  }) =>
    visible ? (
      <section role="dialog" aria-label={title}>
        {children}
        {footer}
      </section>
    ) : null,
  Button: ({ children, onPress, isDisabled, isLoading }: any) => (
    <button disabled={isDisabled || isLoading} onClick={onPress}>
      {children}
    </button>
  ),
  Checkbox: ({ isSelected, isDisabled, onValueChange, ...props }: any) => (
    <input
      type="checkbox"
      aria-label={props["aria-label"]}
      checked={isSelected}
      disabled={isDisabled}
      onChange={(e) => onValueChange(e.target.checked)}
    />
  ),
  Chip: ({ children }: any) => <span>{children}</span>,
  Tooltip: ({ children }: any) => children,
  Spinner: () => <span>checking</span>,
  Progress: ({ value, maxValue }: any) => <progress value={value} max={maxValue} />,
  Tabs: ({ children, onSelectionChange, isDisabled }: any) => (
    <div>
      {Array.isArray(children) &&
        children.map((child: any) => (
          <button
            disabled={isDisabled}
            key={child.key}
            onClick={() => onSelectionChange(child.key)}
          >
            {child.props.title}
          </button>
        ))}
    </div>
  ),
  Tab: () => null,
  Pagination: () => null,
}));
const baseRow: SourceAvailabilityRow = {
  key: "dead",
  taskId: 42,
  taskTitle: "Resource A",
  partId: "p",
  sourceId: "s",
  url: "https://pan.baidu.com/s/original",
  accessCode: "abcd",
  disabled: false,
  inUse: false,
  actionable: true,
  health: { status: "confirmedUnavailable", checkedAt: "2026-01-01T00:00:00Z" },
};
const preview = (rows: SourceAvailabilityRow[] = [baseRow]): SourceAvailabilityPreview => ({
  batchId: "snapshot-a",
  status: "ready",
  processed: rows.length,
  total: rows.length,
  taskCount: 1,
  sourceCount: rows.length,
  rows,
  expiresAt: new Date(Date.now() + 60000).toISOString(),
});
beforeEach(() => {
  vi.clearAllMocks();
  api.preview.mockResolvedValue(preview());
  api.apply.mockResolvedValue({ changedCount: 1, skippedCount: 0 });
});
afterEach(cleanup);
const show = () => render(<SourceAvailabilityReviewModal taskIds={[42, 42, 43]} />);
const button = (key: string) =>
  screen.getByRole("button", { name: `sourceAvailability.review.${key}` });
describe("fixed-source review", () => {
  it("only defaults confirmed, eligible, inactive links; exposes URL and code without mutating", async () => {
    api.preview.mockResolvedValue(
      preview([
        baseRow,
        { ...baseRow, key: "unknown", health: { status: "unknown" } },
        { ...baseRow, key: "active", inUse: true },
        { ...baseRow, key: "access", health: { status: "accessRequired" } },
      ]),
    );
    show();
    const boxes = await screen.findAllByRole("checkbox");
    expect(boxes.map((box) => (box as HTMLInputElement).checked)).toEqual([
      true,
      false,
      false,
      false,
    ]);
    expect(boxes.slice(1).every((box) => (box as HTMLInputElement).disabled)).toBe(true);
    expect(screen.getAllByText(baseRow.url).length).toBe(4);
    expect(screen.getAllByText("abcd").length).toBe(4);
    expect(api.preview).toHaveBeenCalledWith([42, 43], false);
    expect(api.apply).not.toHaveBeenCalled();
  });
  it("binds confirmation to snapshot and checked keys, never resubmits consumed batch", async () => {
    const changed = vi.fn();
    render(<SourceAvailabilityReviewModal taskIds={[42]} onChanged={changed} />);
    await screen.findByRole("checkbox");
    fireEvent.click(button("action.disable"));
    expect(api.apply).not.toHaveBeenCalled();
    fireEvent.click(button("confirm.disable"));
    await waitFor(() =>
      expect(api.apply).toHaveBeenCalledExactlyOnceWith("snapshot-a", ["dead"], "disable"),
    );
    await waitFor(() => expect(changed).toHaveBeenCalledTimes(1));
    expect(button("action.disable")).toBeDisabled();
  });
  it("requires a new snapshot after ambiguous apply failure", async () => {
    api.apply.mockRejectedValue(new Error("Connection lost"));
    show();
    await screen.findByRole("checkbox");
    fireEvent.click(button("action.disable"));
    fireEvent.click(button("confirm.disable"));
    expect(await screen.findByRole("alert")).toHaveTextContent("unknownOutcome");
    expect(button("confirm.disable")).toBeDisabled();
    fireEvent.click(button("confirm.disable"));
    expect(api.apply).toHaveBeenCalledTimes(1);
    fireEvent.click(button("refresh"));
    await waitFor(() => expect(api.preview).toHaveBeenCalledTimes(2));
    expect(api.preview).toHaveBeenLastCalledWith([42, 43], true);
  });
  it("closing is harmless and refresh retains original task scope", async () => {
    const view = show();
    await screen.findByRole("checkbox");
    view.rerender(<SourceAvailabilityReviewModal taskIds={[99]} />);
    fireEvent.click(button("refresh"));
    await waitFor(() => expect(api.preview).toHaveBeenCalledTimes(2));
    expect(api.preview).toHaveBeenLastCalledWith([42, 43], true);
    fireEvent.click(screen.getByRole("button", { name: "common.action.close" }));
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(api.apply).not.toHaveBeenCalled();
  });
  it("does not submit an expired preview", async () => {
    api.preview.mockResolvedValue({ ...preview(), expiresAt: "2020-01-01T00:00:00Z" });
    show();
    await screen.findByRole("checkbox");
    expect(button("action.disable")).toBeDisabled();
    expect(screen.getByRole("alert")).toHaveTextContent("expired");
    expect(api.apply).not.toHaveBeenCalled();
  });
  it("restores disabled links only after explicit confirmation", async () => {
    api.preview.mockResolvedValue(
      preview([{ ...baseRow, disabled: true, key: "disabled", health: { status: "unknown" } }]),
    );
    show();
    await waitFor(() => expect(api.preview).toHaveBeenCalled());
    fireEvent.click(button("tab.restore"));
    await screen.findByRole("checkbox");
    fireEvent.click(button("action.restore"));
    expect(api.apply).not.toHaveBeenCalled();
    fireEvent.click(button("confirm.restore"));
    await waitFor(() =>
      expect(api.apply).toHaveBeenCalledExactlyOnceWith("snapshot-a", ["disabled"], "restore"),
    );
  });
  it("uses one initial check even when StrictMode replays effects", async () => {
    render(
      <StrictMode>
        <SourceAvailabilityReviewModal taskIds={[42]} />
      </StrictMode>,
    );
    await screen.findByRole("checkbox");
    expect(api.preview).toHaveBeenCalledTimes(1);
  });
  it("shows the actual selected sources again during confirmation even after filtering", async () => {
    api.preview.mockResolvedValue(
      preview([
        baseRow,
        {
          ...baseRow,
          key: "unknown",
          health: { status: "unknown" },
          url: "https://example.com/unknown",
        },
      ]),
    );
    show();
    await screen.findAllByRole("checkbox");
    fireEvent.click(screen.getByRole("button", { name: /sourceAvailability.status.unknown/ }));
    expect(screen.queryByText(baseRow.url)).not.toBeInTheDocument();
    fireEvent.click(button("action.disable"));
    expect(screen.getByText(baseRow.url)).toBeInTheDocument();
    expect(screen.queryByText("https://example.com/unknown")).not.toBeInTheDocument();
    expect(api.apply).not.toHaveBeenCalled();
  });
  it("locks repeated refresh clicks and old selections while a fresh snapshot is pending", async () => {
    let finish!: (value: SourceAvailabilityPreview) => void;
    api.preview.mockResolvedValueOnce(preview()).mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          finish = resolve;
        }),
    );
    show();
    await screen.findByRole("checkbox");
    fireEvent.click(button("refresh"));
    fireEvent.click(button("refresh"));
    expect(api.preview).toHaveBeenCalledTimes(2);
    expect(button("action.disable")).toBeDisabled();
    expect(screen.queryByRole("checkbox")).not.toBeInTheDocument();
    await act(async () =>
      finish({ ...preview(), batchId: "snapshot-b", rows: [{ ...baseRow, key: "replacement" }] }),
    );
    fireEvent.click(button("action.disable"));
    fireEvent.click(button("confirm.disable"));
    await waitFor(() =>
      expect(api.apply).toHaveBeenCalledExactlyOnceWith("snapshot-b", ["replacement"], "disable"),
    );
  });
  it("polls metadata-check jobs and locks apply until ready", async () => {
    api.preview.mockResolvedValue({ ...preview(), status: "checking", processed: 0 });
    api.batch.mockResolvedValue(preview());
    show();
    expect(await screen.findByRole("progressbar")).toHaveAttribute("value", "0");
    expect(button("action.disable")).toBeDisabled();
    await waitFor(() => expect(screen.getByRole("checkbox")).toBeChecked(), { timeout: 2500 });
    expect(api.batch).toHaveBeenCalledExactlyOnceWith("snapshot-a");
    expect(api.apply).not.toHaveBeenCalled();
  });
});
