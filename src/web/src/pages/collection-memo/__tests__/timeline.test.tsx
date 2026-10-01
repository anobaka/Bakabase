import type { ReactNode } from "react";
import type { CollectionMemoTarget } from "../helpers";

import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import Timeline from "../components/Timeline";
import {
  clampCoverageBoundary,
  getTimelineCoverage,
  getTimelineDomain,
  getCoverageResizeBounds,
  getTimelineRegions,
  getTimestampTicks,
} from "../helpers";

vi.mock("@/components/bakaui", () => ({
  Button: ({
    children,
    onPress,
    isDisabled,
    isLoading,
  }: {
    children?: ReactNode;
    onPress?: () => void;
    isDisabled?: boolean;
    isLoading?: boolean;
  }) => (
    <button disabled={isDisabled || isLoading} type="button" onClick={onPress}>
      {children}
    </button>
  ),
  Tooltip: ({ children }: { children?: ReactNode }) => <>{children}</>,
}));

const base = Date.parse("2026-09-01T00:00:00.000Z");
const minute = 60_000;
const iso = (minutes: number) => new Date(base + minutes * minute).toISOString();
const domain = { start: base, end: base + 100 * minute };
const record = (id: number, start: number, end: number) => ({
  id,
  startAt: iso(start),
  endAt: iso(end),
});
const target: CollectionMemoTarget = {
  id: 1,
  name: "Target",
  ranges: [record(1, 20, 40), record(2, 60, 80)],
};
const pointer = { pointerId: 7, pointerType: "mouse", button: 0 };
const pointerDown = (element: HTMLElement, clientX: number) =>
  fireEvent.pointerDown(element, { ...pointer, clientX });
const pointerMove = (element: HTMLElement, clientX: number) =>
  fireEvent.pointerMove(element, { ...pointer, clientX });
const pointerUp = (element: HTMLElement, clientX: number) =>
  fireEvent.pointerUp(element, { ...pointer, clientX });
const trackElement = () => document.querySelector<HTMLDivElement>("[data-collection-memo-track]")!;
const endHandles = () =>
  screen.getAllByRole("slider", { name: "collectionMemo.timeline.resizeEnd" });
const startHandles = () =>
  screen.getAllByRole("slider", { name: "collectionMemo.timeline.resizeStart" });
const gaps = () =>
  screen.getAllByRole("button", { name: "collectionMemo.timeline.uncollectedRange" });

let capture: ReturnType<typeof vi.fn>;
let release: ReturnType<typeof vi.fn>;
const oldCapture = Object.getOwnPropertyDescriptor(HTMLElement.prototype, "setPointerCapture");
const oldRelease = Object.getOwnPropertyDescriptor(HTMLElement.prototype, "releasePointerCapture");
const oldHas = Object.getOwnPropertyDescriptor(HTMLElement.prototype, "hasPointerCapture");

beforeEach(() => {
  vi.stubGlobal(
    "PointerEvent",
    class extends MouseEvent {
      pointerId: number;
      pointerType: string;
      constructor(type: string, init: PointerEventInit = {}) {
        super(type, init);
        this.pointerId = init.pointerId ?? 0;
        this.pointerType = init.pointerType ?? "mouse";
      }
    },
  );
  const captured = new WeakMap<HTMLElement, number>();

  capture = vi.fn(function (this: HTMLElement, id: number) {
    captured.set(this, id);
  });
  release = vi.fn(function (this: HTMLElement) {
    captured.delete(this);
  });
  Object.defineProperty(HTMLElement.prototype, "setPointerCapture", {
    configurable: true,
    value: capture,
  });
  Object.defineProperty(HTMLElement.prototype, "releasePointerCapture", {
    configurable: true,
    value: release,
  });
  Object.defineProperty(HTMLElement.prototype, "hasPointerCapture", {
    configurable: true,
    value: function (this: HTMLElement, id: number) {
      return captured.get(this) === id;
    },
  });
  vi.spyOn(HTMLElement.prototype, "getBoundingClientRect").mockImplementation(function (
    this: HTMLElement,
  ) {
    const top = 100;
    const isCard = this.getAttribute("role") === "dialog";
    const left = 100 + (Number.parseFloat(this.style.left) || 0) * 10;
    const width = isCard
      ? 320
      : this.hasAttribute("data-collection-memo-track")
        ? 1000
        : (Number.parseFloat(this.style.width) || 0) * 10;
    const height = isCard ? 112 : 16;

    return {
      top,
      bottom: top + height,
      left,
      right: left + width,
      width,
      height,
      x: left,
      y: top,
      toJSON: () => ({}),
    };
  });
});

afterEach(() => {
  cleanup();
  vi.useRealTimers();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
  for (const [key, descriptor] of [
    ["setPointerCapture", oldCapture],
    ["releasePointerCapture", oldRelease],
    ["hasPointerCapture", oldHas],
  ] as const) {
    if (descriptor) Object.defineProperty(HTMLElement.prototype, key, descriptor);
    else delete (HTMLElement.prototype as unknown as Record<string, unknown>)[key];
  }
});

const showTimeline = (
  onResizeCoverage = vi.fn().mockResolvedValue(undefined),
  onFillGap = vi.fn().mockResolvedValue(undefined),
  selectedTarget = target,
) => {
  const props = {
    target: selectedTarget,
    domain,
    formatDate: (date: string | number) => new Date(date).toISOString(),
    onResizeCoverage,
    onFillGap,
  };
  const view = render(<Timeline {...props} />);

  return { ...view, props, onResizeCoverage, onFillGap };
};

describe("timeline coverage precision", () => {
  it("keeps the exact global earliest tick without exposing a fake leading gap", () => {
    const early = {
      ...target,
      id: 1,
      ranges: [{ id: 1, startAt: "2026-09-01T00:00:00.1000001Z", endAt: iso(40) }],
    };
    const later = {
      ...target,
      id: 2,
      ranges: [{ id: 2, startAt: "2026-09-01T00:00:00.1000002Z", endAt: iso(40) }],
    };
    const preciseDomain = getTimelineDomain([later, early], domain.end);
    const coverage = getTimelineCoverage(early.ranges);

    expect(preciseDomain.startAt).toBe(early.ranges[0].startAt);
    expect(getTimelineRegions(coverage, preciseDomain)[0]).toMatchObject({
      collected: true,
      startAt: early.ranges[0].startAt,
    });
    expect(getCoverageResizeBounds(coverage, 0, preciseDomain, "start").min).toBe(
      early.ranges[0].startAt,
    );
  });
  it("keeps a 100ns gap separate and supplies exact original component snapshots", () => {
    const records = [
      { id: 1, startAt: "2026-09-01T00:00:00.1000000Z", endAt: "2026-09-01T00:00:00.1234567Z" },
      { id: 2, startAt: "2026-09-01T00:00:00.1234568Z", endAt: "2026-09-01T00:00:00.2000000Z" },
    ];
    const coverage = getTimelineCoverage(records);

    expect(coverage.map((component) => component.ranges.map((range) => range.id))).toEqual([
      [1],
      [2],
    ]);
    expect(getTimestampTicks(records[1].startAt)! - getTimestampTicks(records[0].endAt)!).toBe(1n);
    expect(
      getTimelineRegions(coverage, { start: base, end: base + 1000 }).find(
        (region) => !region.collected && region.startAt === records[0].endAt,
      ),
    ).toMatchObject({ endAt: records[1].startAt });
  });

  it("normalizes explicit offsets while keeping exact ticks", () => {
    expect(getTimestampTicks("2026-09-01T08:00:00.1234567+08:00")).toBe(
      getTimestampTicks("2026-09-01T00:00:00.1234567Z"),
    );
  });

  it("clamps after rounding to exact timestamps, keeping precise bound strings", () => {
    const min = "2026-09-01T00:00:00.1234567Z";
    const max = "2026-09-01T00:00:00.1254567Z";

    expect(clampCoverageBoundary(Date.parse(min) + 0.1, { min, max })).toBe(min);
    expect(clampCoverageBoundary(Date.parse(max) + 1, { min, max })).toBe(max);
  });
});

describe("timeline gap hover actions", () => {
  it.each(["{Enter}", " ", "{ArrowDown}"])(
    "reaches and activates the fill action by Tab then %s",
    async (key) => {
      const user = userEvent.setup();
      const { onFillGap } = showTimeline();

      await user.tab();
      expect(gaps()[0]).toHaveFocus();
      await user.keyboard(key);
      const action = within(screen.getByRole("dialog")).getByRole("button");

      expect(action).toHaveFocus();
      expect(onFillGap).not.toHaveBeenCalled();
      await user.keyboard("{Escape}");
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      expect(gaps()[0]).toHaveFocus();
      await user.keyboard(key);
      expect(within(screen.getByRole("dialog")).getByRole("button")).toHaveFocus();
      await user.keyboard("{Enter}");
      expect(onFillGap).toHaveBeenCalledExactlyOnceWith({ startAt: iso(0), endAt: iso(20) });
    },
  );

  it("opens on pointer hover without taking focus from another control", async () => {
    const user = userEvent.setup();

    render(
      <>
        <button>Before timeline</button>
        <Timeline domain={domain} formatDate={String} target={target} onFillGap={vi.fn()} />
      </>,
    );
    await user.tab();
    fireEvent.pointerEnter(gaps()[1]);

    expect(screen.getByRole("button", { name: "Before timeline" })).toHaveFocus();
    expect(screen.getByRole("dialog")).toBeInTheDocument();
  });
  it("keeps the action reachable across the hover-card gap and fills the exact interval", async () => {
    vi.useFakeTimers();
    const { onFillGap } = showTimeline();
    const gap = gaps()[1];

    fireEvent.pointerEnter(gap);
    const card = screen.getByRole("dialog");

    fireEvent.pointerLeave(gap);
    act(() => vi.advanceTimersByTime(100));
    fireEvent.pointerEnter(card);
    act(() => vi.advanceTimersByTime(250));
    expect(card).toBeInTheDocument();
    await act(async () =>
      fireEvent.click(
        within(card).getByRole("button", { name: "collectionMemo.timeline.fillGap" }),
      ),
    );
    expect(onFillGap).toHaveBeenCalledExactlyOnceWith({ startAt: iso(40), endAt: iso(60) });
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("supports touch click and keeps a focused action open after the pointer leaves", () => {
    vi.useFakeTimers();
    showTimeline();
    fireEvent.click(gaps()[1]);
    const card = screen.getByRole("dialog");

    act(() => within(card).getByRole("button").focus());
    fireEvent.pointerLeave(card);
    act(() => vi.advanceTimersByTime(500));
    expect(card).toBeInTheDocument();
    fireEvent.keyDown(document, { key: "Escape" });
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(gaps()[1]).toHaveFocus();
  });

  it("flips the action surface upward to stay within a low viewport", () => {
    vi.stubGlobal("innerHeight", 160);
    showTimeline();
    fireEvent.pointerEnter(gaps()[1]);

    expect(screen.getByRole("dialog")).toHaveStyle({ top: "16px" });
  });

  it("blocks duplicate saves and keeps a failed gap action visible for retry", async () => {
    let finish!: () => void;
    const onFillGap = vi
      .fn()
      .mockReturnValueOnce(
        new Promise<void>((resolve) => {
          finish = resolve;
        }),
      )
      .mockRejectedValueOnce(new Error("Failed"))
      .mockResolvedValue(undefined);

    showTimeline(undefined, onFillGap);
    fireEvent.click(gaps()[1]);
    let action = within(screen.getByRole("dialog")).getByRole("button");

    fireEvent.click(action);
    expect(action).toBeDisabled();
    fireEvent.click(action);
    expect(onFillGap).toHaveBeenCalledTimes(1);
    await act(async () => finish());
    fireEvent.click(gaps()[1]);
    action = within(screen.getByRole("dialog")).getByRole("button");
    await act(async () => fireEvent.click(action));
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(within(screen.getByRole("dialog")).getByRole("alert")).toHaveTextContent(
      "collectionMemo.timeline.saveFailed",
    );
    await act(async () => fireEvent.click(within(screen.getByRole("dialog")).getByRole("button")));
    expect(onFillGap).toHaveBeenCalledTimes(3);
  });
});

describe("timeline boundary gestures", () => {
  it("captures the stable track, previews the dates and colors, then commits once on release", async () => {
    const { onResizeCoverage } = showTimeline();
    const track = trackElement();

    pointerDown(endHandles()[0], 500);
    expect(capture).toHaveBeenCalledExactlyOnceWith(7);
    expect(capture.mock.instances[0]).toBe(track);
    pointerMove(track, 600);
    expect(endHandles()[0]).toHaveAttribute("aria-valuenow", String(base + 50 * minute));
    expect(
      screen.getAllByRole("button", { name: "collectionMemo.timeline.collectedRange" })[0],
    ).toHaveStyle({ width: "30%" });
    expect(onResizeCoverage).not.toHaveBeenCalled();
    await act(async () => pointerUp(track, 600));
    expect(onResizeCoverage).toHaveBeenCalledExactlyOnceWith({
      ranges: [target.ranges[0]],
      edge: "end",
      at: iso(50),
    });
    pointerUp(track, 600);
    expect(onResizeCoverage).toHaveBeenCalledTimes(1);
    expect(release).toHaveBeenCalledExactlyOnceWith(7);
  });

  it("uses the final release position and sends all overlapping component ids", async () => {
    const overlap = {
      ...target,
      ranges: [record(1, 20, 35), record(3, 30, 40), record(2, 60, 80)],
    };
    const { onResizeCoverage } = showTimeline(undefined, undefined, overlap);

    pointerDown(endHandles()[0], 500);
    pointerMove(trackElement(), 550);
    await act(async () => pointerUp(trackElement(), 600));
    expect(onResizeCoverage).toHaveBeenCalledExactlyOnceWith({
      ranges: [overlap.ranges[0], overlap.ranges[1]],
      edge: "end",
      at: iso(50),
    });
  });

  it("freezes the domain and geometry while an active gesture crosses a timer/layout update", async () => {
    const { props, rerender, onResizeCoverage } = showTimeline();

    pointerDown(endHandles()[0], 500);
    rerender(<Timeline {...props} domain={{ ...domain, end: domain.end + 100 * minute }} />);
    vi.mocked(HTMLElement.prototype.getBoundingClientRect).mockReturnValue({
      width: 2000,
    } as DOMRect);
    pointerMove(trackElement(), 600);
    expect(document.querySelectorAll("time")[1]).toHaveAttribute("dateTime", iso(100));
    await act(async () => pointerUp(trackElement(), 600));
    expect(onResizeCoverage.mock.calls[0][0].at).toBe(iso(50));
  });

  it.each(["Escape", "pointercancel", "lostpointercapture"])(
    "cancels without saving on %s",
    (method) => {
      const { onResizeCoverage } = showTimeline();

      pointerDown(endHandles()[0], 500);
      pointerMove(trackElement(), 600);
      if (method === "Escape") fireEvent.keyDown(document, { key: "Escape" });
      else
        fireEvent(
          trackElement(),
          new PointerEvent(method, { ...pointer, clientX: 600, bubbles: true }),
        );
      pointerUp(trackElement(), 600);
      expect(onResizeCoverage).not.toHaveBeenCalled();
      expect(endHandles()[0]).toHaveAttribute("aria-valuenow", String(base + 40 * minute));
    },
  );

  it("does not quantize or save an unchanged precise boundary on a mere press", () => {
    const preciseTarget = {
      ...target,
      ranges: [{ ...target.ranges[0], endAt: "2026-09-01T00:40:00.1234567Z" }],
    };
    const { onResizeCoverage } = showTimeline(undefined, undefined, preciseTarget);

    pointerDown(endHandles()[0], 500);
    pointerMove(trackElement(), 501);
    pointerUp(trackElement(), 500);
    expect(onResizeCoverage).not.toHaveBeenCalled();
  });

  it("cancels when records change or external saving starts, clearing the frozen preview", () => {
    const { props, rerender, onResizeCoverage } = showTimeline();

    pointerDown(endHandles()[0], 500);
    pointerMove(trackElement(), 600);
    rerender(<Timeline {...props} isSaving />);
    pointerUp(trackElement(), 600);
    expect(onResizeCoverage).not.toHaveBeenCalled();
    expect(endHandles()[0]).toHaveAttribute("aria-valuenow", String(base + 40 * minute));
    rerender(<Timeline {...props} />);
    pointerDown(endHandles()[0], 500);
    pointerMove(trackElement(), 600);
    rerender(
      <Timeline {...props} target={{ ...target, ranges: [record(1, 20, 45), target.ranges[1]] }} />,
    );
    pointerUp(trackElement(), 600);
    expect(onResizeCoverage).not.toHaveBeenCalled();
    expect(screen.getByRole("alert")).toHaveTextContent("collectionMemo.timeline.changed");
    expect(endHandles()[0]).toHaveAttribute("aria-valuenow", String(base + 45 * minute));
  });

  it("clamps to the neighboring component, opposite edge, and current time", async () => {
    const { onResizeCoverage } = showTimeline();

    pointerDown(endHandles()[0], 500);
    await act(async () => pointerUp(trackElement(), 1500));
    expect(onResizeCoverage.mock.calls[0][0].at).toBe(iso(60));
    pointerDown(startHandles()[0], 300);
    await act(async () => pointerUp(trackElement(), 1500));
    expect(onResizeCoverage.mock.calls[1][0].at).toBe(iso(40));
    pointerDown(endHandles()[1], 900);
    await act(async () => pointerUp(trackElement(), 1500));
    expect(onResizeCoverage.mock.calls[2][0].at).toBe(iso(100));
  });

  it("keeps precise neighboring boundaries and excludes a separated tick-level neighbor", async () => {
    const precise = {
      ...target,
      ranges: [
        { id: 1, startAt: iso(20), endAt: "2026-09-01T00:40:00.1234567Z" },
        { id: 2, startAt: "2026-09-01T00:40:00.1234568Z", endAt: iso(80) },
      ],
    };
    const { onResizeCoverage } = showTimeline(undefined, undefined, precise);

    pointerDown(endHandles()[0], 500);
    await act(async () => pointerUp(trackElement(), 900));
    expect(onResizeCoverage).toHaveBeenCalledExactlyOnceWith({
      ranges: [precise.ranges[0]],
      edge: "end",
      at: precise.ranges[1].startAt,
    });
  });

  it("supports minute/hour arrow adjustments and Home/End limits with no writes at an unchanged limit", async () => {
    const { onResizeCoverage } = showTimeline();

    await act(async () => fireEvent.keyDown(endHandles()[0], { key: "ArrowRight" }));
    expect(onResizeCoverage.mock.calls[0][0].at).toBe(iso(41));
    await act(async () => fireEvent.keyDown(endHandles()[0], { key: "ArrowLeft", shiftKey: true }));
    expect(onResizeCoverage.mock.calls[1][0].at).toBe(iso(20));
    await act(async () => fireEvent.keyDown(startHandles()[0], { key: "Home" }));
    expect(onResizeCoverage.mock.calls[2][0].at).toBe(iso(0));
    await act(async () => fireEvent.keyDown(endHandles()[1], { key: "End" }));
    expect(onResizeCoverage.mock.calls[3][0].at).toBe(iso(100));
  });

  it("offers a failed resize retry and disables further gestures until saving finishes", async () => {
    const onResizeCoverage = vi
      .fn()
      .mockRejectedValueOnce(new Error("Failed"))
      .mockResolvedValue(undefined);

    showTimeline(onResizeCoverage);
    pointerDown(endHandles()[0], 500);
    await act(async () => pointerUp(trackElement(), 600));
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "collectionMemo.timeline.saveFailed",
    );
    await act(async () =>
      fireEvent.click(screen.getByRole("button", { name: "collectionMemo.timeline.retry" })),
    );
    expect(onResizeCoverage).toHaveBeenCalledTimes(2);
    expect(onResizeCoverage.mock.calls[1][0]).toEqual(onResizeCoverage.mock.calls[0][0]);
    await waitFor(() => expect(screen.queryByRole("alert")).not.toBeInTheDocument());
  });

  it("requires a fresh operation after a stale snapshot conflict instead of retrying the old snapshot", async () => {
    const onResizeCoverage = vi
      .fn()
      .mockRejectedValue(Object.assign(new Error("Changed"), { code: 409 }));

    showTimeline(onResizeCoverage);
    pointerDown(endHandles()[0], 500);
    await act(async () => pointerUp(trackElement(), 600));

    expect(screen.getByRole("alert")).toHaveTextContent("collectionMemo.timeline.changed");
    expect(
      screen.queryByRole("button", { name: "collectionMemo.timeline.retry" }),
    ).not.toBeInTheDocument();
    expect(onResizeCoverage).toHaveBeenCalledTimes(1);
  });

  it("blocks additional pointer and keyboard mutations while a resize is pending", async () => {
    let finish!: () => void;
    const onResizeCoverage = vi.fn().mockReturnValue(
      new Promise<void>((resolve) => {
        finish = resolve;
      }),
    );

    showTimeline(onResizeCoverage);
    pointerDown(endHandles()[0], 500);
    pointerUp(trackElement(), 600);
    expect(endHandles()[0]).toHaveAttribute("aria-disabled", "true");
    pointerDown(endHandles()[1], 900);
    pointerMove(trackElement(), 950);
    pointerUp(trackElement(), 950);
    fireEvent.keyDown(endHandles()[1], { key: "ArrowLeft" });
    expect(onResizeCoverage).toHaveBeenCalledTimes(1);
    await act(async () => finish());
  });

  it("does not save keyboard limits that already equal the original boundary", () => {
    const full = { ...target, ranges: [record(1, 0, 100)] };
    const { onResizeCoverage } = showTimeline(undefined, undefined, full);

    fireEvent.keyDown(startHandles()[0], { key: "Home" });
    fireEvent.keyDown(endHandles()[0], { key: "End" });
    expect(onResizeCoverage).not.toHaveBeenCalled();
  });

  it("keeps both point handles accessible and never inverts a point while resizing", async () => {
    const point = { ...target, ranges: [record(1, 50, 50)] };
    const { onResizeCoverage } = showTimeline(undefined, undefined, point);

    expect(startHandles()).toHaveLength(1);
    expect(endHandles()).toHaveLength(1);
    pointerDown(startHandles()[0], 600);
    pointerUp(trackElement(), 700);
    expect(onResizeCoverage).not.toHaveBeenCalled();
    pointerDown(endHandles()[0], 600);
    await act(async () => pointerUp(trackElement(), 700));
    expect(onResizeCoverage).toHaveBeenCalledExactlyOnceWith({
      ranges: point.ranges,
      edge: "end",
      at: iso(60),
    });
  });
});
