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

const openUrl = vi.hoisted(() => vi.fn());

vi.mock("@/sdk/BApi", () => ({
  default: { gui: { openUrlInDefaultBrowser: openUrl } },
}));

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
  openUrl.mockClear();
  vi.stubGlobal(
    "ResizeObserver",
    class {
      observe() {}
      disconnect() {}
    },
  );
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
    const isCard = ["dialog", "tooltip"].includes(this.getAttribute("role") ?? "");
    const left = 100 + (Number.parseFloat(this.style.left) || 0) * 10;
    const width = isCard
      ? 320
      : this.hasAttribute("data-collection-memo-track")
        ? 1000
        : (Number.parseFloat(this.style.width) || 0) * 10;
    const height = isCard ? Math.min(112, Number.parseFloat(this.style.maxHeight) || 112) : 16;

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
    reverse: false,
    formatDate: (date: string | number) => new Date(date).toISOString(),
    onResizeCoverage,
    onFillGap,
  };
  const view = render(<Timeline {...props} />);

  return { ...view, props, onResizeCoverage, onFillGap };
};

describe("timeline range metadata", () => {
  it.each([20, 20.5])(
    "opens linked point/short interval on a boundary click without a resize (end=%s)",
    (end) => {
      const annotated = { ...record(1, 20, end), url: "https://example.com/short" };
      const { onResizeCoverage } = showTimeline(undefined, undefined, {
        ...target,
        ranges: [annotated],
      });

      pointerDown(endHandles()[0], 300);
      pointerUp(trackElement(), 301);
      expect(openUrl).toHaveBeenCalledExactlyOnceWith({ url: annotated.url });
      expect(onResizeCoverage).not.toHaveBeenCalled();
    },
  );

  it("resizes a linked interval without opening its URL when the pointer moves", async () => {
    const annotated = {
      ...record(1, 20, 40),
      url: "https://example.com/source",
      note: "Keep note",
    };
    const { onResizeCoverage } = showTimeline(undefined, undefined, {
      ...target,
      ranges: [annotated],
    });

    pointerDown(endHandles()[0], 500);
    pointerMove(trackElement(), 600);
    await act(async () => pointerUp(trackElement(), 600));
    expect(openUrl).not.toHaveBeenCalled();
    expect(onResizeCoverage).toHaveBeenCalledExactlyOnceWith({
      ranges: [annotated],
      edge: "end",
      at: iso(50),
    });
  });

  it("does not open a source when a drag returns to its original position", () => {
    const annotated = { ...record(1, 20, 40), url: "https://example.com/source" };
    const { onResizeCoverage } = showTimeline(undefined, undefined, {
      ...target,
      ranges: [annotated],
    });

    pointerDown(endHandles()[0], 500);
    pointerMove(trackElement(), 600);
    pointerUp(trackElement(), 500);
    expect(openUrl).not.toHaveBeenCalled();
    expect(onResizeCoverage).not.toHaveBeenCalled();
  });

  it("opens original-source choices on a merged boundary click", async () => {
    const first = { ...record(1, 20, 25), url: "https://example.com/first" };
    const second = { ...record(2, 25, 30), url: "https://example.com/second" };

    showTimeline(undefined, undefined, { ...target, ranges: [first, second] });
    pointerDown(endHandles()[0], 400);
    pointerUp(trackElement(), 400);
    await waitFor(() => expect(screen.getByRole("dialog").querySelectorAll("a")).toHaveLength(2));
    expect(openUrl).not.toHaveBeenCalled();
  });

  it.each([20, 20.5])(
    "keeps short and point range links accessible through boundary details (end=%s)",
    (end) => {
      const annotated = {
        ...record(1, 20, end),
        url: "https://example.com/short",
        note: "Short source",
      };
      const { onResizeCoverage } = showTimeline(undefined, undefined, {
        ...target,
        ranges: [annotated],
      });
      const boundary = endHandles()[0];

      fireEvent.pointerEnter(boundary);
      expect(screen.getByRole("dialog")).toHaveTextContent(annotated.note);
      fireEvent.keyDown(boundary, { key: "Enter" });
      const link = screen.getByRole("dialog").querySelector("a")!;

      expect(link).toHaveFocus();
      fireEvent.click(link);
      expect(openUrl).toHaveBeenCalledExactlyOnceWith({ url: annotated.url });
      expect(onResizeCoverage).not.toHaveBeenCalled();
    },
  );

  it("opens a single linked interval directly and keeps its note in the hover details", () => {
    const annotated = {
      ...record(1, 20, 40),
      url: "https://example.com/first",
      note: "Saved source\nSecond line",
    };

    showTimeline(undefined, undefined, { ...target, ranges: [annotated] });
    const link = trackElement().querySelector<HTMLElement>("a")!;

    expect(link).toHaveStyle({ left: "20%", width: "20%" });
    fireEvent.click(link);
    expect(openUrl).toHaveBeenCalledExactlyOnceWith({ url: annotated.url });
    expect(screen.getByRole("dialog")).toHaveTextContent("Saved source");
    expect(screen.getByRole("dialog")).toHaveTextContent("Second line");
  });

  it("offers the original links for merged coverage without opening an arbitrary link", async () => {
    const first = { ...record(1, 20, 45), url: "https://example.com/first", note: "First source" };
    const second = {
      ...record(2, 35, 60),
      url: "https://example.com/second",
      note: "Second source",
    };

    showTimeline(undefined, undefined, { ...target, ranges: [first, second] });
    const segment = screen.getByRole("button", { name: "collectionMemo.timeline.collectedRange" });

    expect(segment).toHaveStyle({ left: "20%", width: "40%" });
    fireEvent.keyDown(segment, { key: "ArrowDown" });
    const details = screen.getByRole("dialog");
    const links = details.querySelectorAll("a");

    expect(openUrl).not.toHaveBeenCalled();
    expect(links).toHaveLength(2);
    expect(links[0]).toHaveFocus();
    fireEvent.click(links[1]);
    expect(openUrl).toHaveBeenCalledExactlyOnceWith({ url: second.url });
    fireEvent.keyDown(document, { key: "Escape" });
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(segment).toHaveFocus();
  });
});

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

    const card = screen.getByRole("dialog");
    const top = Number.parseFloat(card.style.top);
    const height = card.getBoundingClientRect().height;

    expect(top).toBeGreaterThanOrEqual(8);
    expect(top + height).toBeLessThanOrEqual(92);
    fireEvent.pointerEnter(gaps()[1]);
    fireEvent.focus(gaps()[1]);
    expect(card.style.top).toBe(`${top}px`);
  });

  it("keeps the trailing gap card and focused action mounted across clock and parent updates", async () => {
    const { props, rerender, onFillGap } = showTimeline();

    fireEvent.click(gaps()[2]);
    const card = screen.getByRole("dialog");
    const action = within(card).getByRole("button");

    act(() => action.focus());
    for (let tick = 1; tick <= 3; tick++) {
      rerender(
        <Timeline
          {...props}
          domain={{ ...domain, end: domain.end + tick * minute }}
          formatDate={(date) => new Date(date).toISOString()}
        />,
      );
      expect(screen.getByRole("dialog")).toBe(card);
      expect(within(card).getByRole("button")).toBe(action);
      expect(action).toHaveFocus();
    }
    await act(async () => fireEvent.click(action));
    expect(onFillGap).toHaveBeenCalledExactlyOnceWith({ startAt: iso(80), endAt: iso(103) });
  });

  it("uses one stable hover surface while moving between collected ranges, boundaries, and gaps", () => {
    vi.useFakeTimers();
    showTimeline();
    const collected = screen.getAllByRole("button", {
      name: "collectionMemo.timeline.collectedRange",
    })[0];

    fireEvent.pointerEnter(collected);
    const card = screen.getByRole("tooltip");

    expect(card).toHaveTextContent("collectionMemo.timeline.collectedRange");
    fireEvent.pointerLeave(collected);
    fireEvent.pointerEnter(endHandles()[0]);
    expect(screen.getByRole("tooltip")).toBe(card);
    fireEvent.pointerLeave(endHandles()[0]);
    fireEvent.pointerEnter(gaps()[1]);
    expect(screen.getByRole("dialog")).toBe(card);
    expect(card).toHaveTextContent("collectionMemo.timeline.uncollectedRange");
    act(() => vi.advanceTimersByTime(1000));
    expect(screen.getByRole("dialog")).toBe(card);
    fireEvent.pointerLeave(gaps()[1]);
    fireEvent.pointerEnter(collected);
    expect(screen.getByRole("tooltip")).toBe(card);
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(document.querySelectorAll('[role="tooltip"], [role="dialog"]')).toHaveLength(1);
  });

  it("keeps popup scrolling open and closes on scrolling the surrounding page", () => {
    showTimeline();
    fireEvent.pointerEnter(gaps()[1]);
    const card = screen.getByRole("dialog");

    fireEvent.scroll(card);
    expect(card).toBeInTheDocument();
    fireEvent.scroll(within(card).getByRole("button"));
    expect(card).toBeInTheDocument();
    fireEvent.scroll(window);
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("preserves a focused fill action when the pointer passes over another hover source", () => {
    showTimeline();
    fireEvent.keyDown(gaps()[1], { key: "Enter" });
    const card = screen.getByRole("dialog");
    const action = within(card).getByRole("button");

    expect(action).toHaveFocus();
    fireEvent.pointerEnter(endHandles()[0]);
    fireEvent.pointerEnter(
      screen.getAllByRole("button", { name: "collectionMemo.timeline.collectedRange" })[0],
    );
    expect(screen.getByRole("dialog")).toBe(card);
    expect(action).toHaveFocus();
    fireEvent.keyDown(document, { key: "Escape" });
    expect(gaps()[1]).toHaveFocus();
  });

  it("dismisses a removed hover source while preserving unrelated collection changes", () => {
    const { props, rerender } = showTimeline();

    fireEvent.pointerEnter(gaps()[1]);
    const card = screen.getByRole("dialog");

    rerender(
      <Timeline {...props} target={{ ...target, ranges: [...target.ranges, record(3, 90, 95)] }} />,
    );
    expect(screen.getByRole("dialog")).toBe(card);
    rerender(<Timeline {...props} target={{ ...target, ranges: [record(1, 20, 80)] }} />);
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("does not leave an orphan card when saving is enabled while the pointer leaves", () => {
    vi.useFakeTimers();
    const { props, rerender } = showTimeline();

    fireEvent.pointerEnter(gaps()[1]);
    rerender(<Timeline {...props} isSaving />);
    fireEvent.pointerLeave(gaps()[1]);
    act(() => vi.advanceTimersByTime(500));
    rerender(<Timeline {...props} />);
    act(() => vi.advanceTimersByTime(500));
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
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
  it("suppresses the boundary hover card during a drag and restores coverage without saving on cancellation", () => {
    const { onResizeCoverage } = showTimeline();

    fireEvent.pointerEnter(endHandles()[0]);
    expect(screen.getByRole("tooltip")).toBeInTheDocument();
    pointerDown(endHandles()[0], 500);
    expect(screen.queryByRole("tooltip")).not.toBeInTheDocument();
    pointerMove(trackElement(), 600);
    fireEvent.keyDown(document, { key: "Escape" });
    pointerUp(trackElement(), 600);
    expect(onResizeCoverage).not.toHaveBeenCalled();
    expect(endHandles()[0]).toHaveAttribute("aria-valuenow", String(base + 40 * minute));
  });

  it("keeps boundary controls transparent, focusable, and described for keyboard adjustment", () => {
    showTimeline();
    for (const handle of [...startHandles(), ...endHandles()]) {
      expect(handle).toHaveAttribute("tabindex", "0");
      expect(handle.className).toContain("cursor-ew-resize");
      expect(handle.className).not.toMatch(/(?:bg-content1|rounded|shadow|border-)/);
      fireEvent.focus(handle);
      expect(screen.getByRole("tooltip")).toHaveTextContent("collectionMemo.timeline.resizeHint");
      fireEvent.blur(handle);
    }
  });

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
    expect(endHandles()[0]).toHaveStyle({ left: "50%" });
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

describe("timeline global start and direction", () => {
  it("keeps resize guidance available to sliders without a visible guidance row", () => {
    showTimeline();
    const hint = screen.getByText("collectionMemo.timeline.resizeHint");

    expect(hint).toHaveClass("sr-only");
    expect(startHandles()[0]).toHaveAttribute("aria-describedby", hint.id);
    expect(endHandles()[0]).toHaveAttribute("aria-describedby", hint.id);
  });

  it("defaults to newest on the left and mirrors intervals, boundary faces, and captions", () => {
    const { props, rerender } = showTimeline();

    rerender(<Timeline {...props} reverse={undefined} />);
    const collected = screen.getAllByRole("button", {
      name: "collectionMemo.timeline.collectedRange",
    });

    expect(collected[0]).toHaveStyle({ left: "60%", width: "20%" });
    expect(collected[1]).toHaveStyle({ left: "20%", width: "20%" });
    expect(startHandles()[0]).toHaveStyle({ left: "80%", transform: "translate(-100%, -50%)" });
    expect(endHandles()[0]).toHaveStyle({ left: "60%", transform: "translate(0, -50%)" });
    expect(document.querySelector("time")?.parentElement?.className).toContain("justify-end");
    expect(document.querySelectorAll("time")).toHaveLength(1);
    expect(document.querySelector("time")).toHaveAttribute("dateTime", iso(0));
    expect(screen.queryByText("collectionMemo.timeline.now")).not.toBeInTheDocument();
  });

  it.each([false, true])(
    "keeps fill chronological and moves dragged edges visually when reverse=%s",
    async (reverse) => {
      const { props, rerender, onFillGap, onResizeCoverage } = showTimeline();

      rerender(<Timeline {...props} reverse={reverse} />);
      fireEvent.click(gaps()[1]);
      await act(async () =>
        fireEvent.click(within(screen.getByRole("dialog")).getByRole("button")),
      );
      expect(onFillGap).toHaveBeenCalledExactlyOnceWith({ startAt: iso(40), endAt: iso(60) });
      pointerDown(endHandles()[0], 500);
      pointerMove(trackElement(), reverse ? 400 : 600);
      expect(endHandles()[0]).toHaveAttribute("aria-valuenow", String(base + 50 * minute));
      expect(endHandles()[0]).toHaveStyle({ left: reverse ? "50%" : "50%" });
      await act(async () => pointerUp(trackElement(), reverse ? 400 : 600));
      expect(onResizeCoverage).toHaveBeenCalledExactlyOnceWith({
        ranges: [target.ranges[0]],
        edge: "end",
        at: iso(50),
      });
    },
  );

  it("reverses horizontal keyboard motion while retaining chronological Home/End and vertical arrows", async () => {
    const { props, rerender, onResizeCoverage } = showTimeline();

    rerender(<Timeline {...props} reverse />);
    await act(async () => fireEvent.keyDown(endHandles()[0], { key: "ArrowLeft" }));
    expect(onResizeCoverage.mock.calls[0][0].at).toBe(iso(41));
    await act(async () => fireEvent.keyDown(endHandles()[0], { key: "ArrowRight" }));
    expect(onResizeCoverage.mock.calls[1][0].at).toBe(iso(39));
    await act(async () => fireEvent.keyDown(endHandles()[0], { key: "Home" }));
    expect(onResizeCoverage.mock.calls[2][0].at).toBe(iso(20));
    await act(async () => fireEvent.keyDown(endHandles()[0], { key: "End" }));
    expect(onResizeCoverage.mock.calls[3][0].at).toBe(iso(60));
    await act(async () => fireEvent.keyDown(endHandles()[0], { key: "ArrowUp" }));
    expect(onResizeCoverage.mock.calls[4][0].at).toBe(iso(41));
    await act(async () => fireEvent.keyDown(endHandles()[0], { key: "ArrowLeft", shiftKey: true }));
    expect(onResizeCoverage.mock.calls[5][0].at).toBe(iso(60));
  });

  it.each(["direction", "global-start"])("cancels an active gesture when %s changes", (change) => {
    const { props, rerender, onResizeCoverage } = showTimeline();

    pointerDown(endHandles()[0], 500);
    pointerMove(trackElement(), 600);
    rerender(
      <Timeline
        {...props}
        {...(change === "direction"
          ? { reverse: true }
          : {
              domain: {
                start: base + 10 * minute,
                end: domain.end,
                startAt: "2026-09-01T08:10:00+08:00",
              },
            })}
      />,
    );
    pointerUp(trackElement(), 600);
    expect(onResizeCoverage).not.toHaveBeenCalled();
    expect(release).toHaveBeenCalledExactlyOnceWith(7);
    expect(endHandles()[0]).toHaveAttribute("aria-valuenow", String(base + 40 * minute));
    expect(screen.getByRole("alert")).toHaveTextContent("collectionMemo.timeline.changed");
  });

  it("freezes reverse direction, geometry, and current-time domain through timer updates", async () => {
    const { props, rerender, onResizeCoverage } = showTimeline();

    rerender(<Timeline {...props} reverse />);
    pointerDown(endHandles()[0], 500);
    rerender(
      <Timeline {...props} reverse domain={{ ...domain, end: domain.end + 100 * minute }} />,
    );
    pointerMove(trackElement(), 400);
    expect(endHandles()[0]).toHaveStyle({ left: "50%" });
    await act(async () => pointerUp(trackElement(), 400));
    expect(onResizeCoverage.mock.calls[0][0].at).toBe(iso(50));
  });

  it("sends nullable original snapshots and the exact global setting for inherited start and end edits", async () => {
    const globalStartAt = "2026-09-01T00:00:00.1234567Z";
    const originalEndAt = "2026-09-01T00:40:00.1234568Z";
    const inherited = {
      ...target,
      ranges: [{ id: 1, startAt: null, endAt: originalEndAt }, record(2, 60, 80)],
    };
    const { props, rerender, onResizeCoverage } = showTimeline(undefined, undefined, inherited);

    rerender(
      <Timeline
        {...props}
        domain={{ ...domain, start: Date.parse(globalStartAt), startAt: globalStartAt }}
      />,
    );
    expect(startHandles()[0]).toHaveAttribute("aria-valuenow", String(Date.parse(globalStartAt)));
    await act(async () => fireEvent.keyDown(endHandles()[0], { key: "ArrowRight" }));
    expect(onResizeCoverage.mock.calls[0][0]).toMatchObject({
      ranges: [{ id: 1, startAt: null, endAt: originalEndAt }],
      edge: "end",
      expectedGlobalStartAt: globalStartAt,
    });
    await act(async () => fireEvent.keyDown(startHandles()[0], { key: "ArrowRight" }));
    expect(onResizeCoverage.mock.calls[1][0]).toMatchObject({
      ranges: [{ id: 1, startAt: null, endAt: originalEndAt }],
      edge: "start",
      expectedGlobalStartAt: globalStartAt,
    });
    expect(inherited.ranges[0].startAt).toBeNull();
    expect(inherited.ranges[0].endAt).toBe(originalEndAt);
  });

  it("updates inherited coverage with global start while preserving a trailing gap hover source", () => {
    const inherited = { ...target, ranges: [{ id: 1, startAt: null, endAt: iso(40) }] };
    const { props, rerender } = showTimeline(undefined, undefined, inherited);

    rerender(<Timeline {...props} domain={{ ...domain, startAt: iso(0) }} />);
    fireEvent.pointerEnter(gaps()[0]);
    const card = screen.getByRole("dialog");

    rerender(
      <Timeline
        {...props}
        domain={{ start: base + 10 * minute, end: domain.end, startAt: iso(10) }}
      />,
    );
    expect(screen.getByRole("dialog")).toBe(card);
    expect(startHandles()[0]).toHaveAttribute("aria-valuenow", String(base + 10 * minute));
    const collected = screen.getByRole("button", {
      name: "collectionMemo.timeline.collectedRange",
    });

    expect(collected).toHaveStyle({ left: "0%" });
    expect(Number.parseFloat(collected.style.width)).toBeCloseTo(100 / 3);
  });

  it("disables retry when an inherited operation's global start has become stale", async () => {
    const onResizeCoverage = vi.fn().mockRejectedValue(new Error("Failed"));
    const inherited = { ...target, ranges: [{ id: 1, startAt: null, endAt: iso(40) }] };
    const { props, rerender } = showTimeline(onResizeCoverage, undefined, inherited);

    rerender(<Timeline {...props} domain={{ ...domain, startAt: iso(0) }} />);
    await act(async () => fireEvent.keyDown(endHandles()[0], { key: "ArrowRight" }));
    expect(
      screen.getByRole("button", { name: "collectionMemo.timeline.retry" }),
    ).toBeInTheDocument();
    rerender(
      <Timeline
        {...props}
        domain={{ start: base + 10 * minute, end: domain.end, startAt: iso(10) }}
      />,
    );
    expect(
      screen.queryByRole("button", { name: "collectionMemo.timeline.retry" }),
    ).not.toBeInTheDocument();
  });

  it.each([false, true])(
    "omits cropped start and off-track controls without rewriting earlier explicit records when reverse=%s",
    async (reverse) => {
      const originalStartAt = "2026-08-31T23:59:59.1234567Z";
      const clipped = {
        ...target,
        ranges: [{ id: 1, startAt: originalStartAt, endAt: iso(40) }, record(2, -30, -20)],
      };
      const { props, rerender, onResizeCoverage } = showTimeline(undefined, undefined, clipped);

      rerender(
        <Timeline
          {...props}
          domain={{ start: base + 20 * minute, end: domain.end, startAt: iso(20) }}
          reverse={reverse}
        />,
      );
      expect(
        screen.queryByRole("slider", { name: "collectionMemo.timeline.resizeStart" }),
      ).not.toBeInTheDocument();
      expect(endHandles()).toHaveLength(1);
      expect(endHandles()[0]).toHaveAttribute("aria-valuemin", String(base + 20 * minute));
      await act(async () => fireEvent.keyDown(endHandles()[0], { key: "Home" }));
      expect(onResizeCoverage).toHaveBeenCalledExactlyOnceWith({
        ranges: [clipped.ranges[0]],
        edge: "end",
        at: iso(20),
      });
      expect(clipped.ranges[0].startAt).toBe(originalStartAt);
    },
  );

  it("mirrors split point handles and keeps empty configured domains usable", () => {
    const point = { ...target, ranges: [record(1, 50, 50)] };
    const { props, rerender } = showTimeline(undefined, undefined, point);

    rerender(<Timeline {...props} reverse />);
    expect(startHandles()[0]).toHaveStyle({ left: "50%", transform: "translate(0, -50%)" });
    expect(endHandles()[0]).toHaveStyle({ left: "50%", transform: "translate(-100%, -50%)" });
    rerender(
      <Timeline
        {...props}
        reverse
        domain={{ ...domain, startAt: iso(0) }}
        target={{ ...target, ranges: [] }}
      />,
    );
    expect(gaps()[0]).toHaveStyle({ left: "0%", width: "100%" });
    expect(screen.queryByRole("slider")).not.toBeInTheDocument();
  });
});
