import type { ResourcesRef } from "..";

import * as React from "react";
import { createRoot } from "react-dom/client";
import { Grid } from "react-virtualized";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import Resources from "..";

const act = (React as unknown as { act: (scope: () => void) => void }).act;

// Keep the real AutoSizer, Grid and CellMeasurer. jsdom only supplies the boxes
// a browser would measure, so assertions exercise the virtualized row offsets.
let viewportWidth = 600;
const viewportHeight = 600;
let hidden = false;
let frames = new Map<number, FrameRequestCallback>();
let nextFrameId = 1;
let container: HTMLDivElement;
let root: ReturnType<typeof createRoot>;
let resourcesRef: React.RefObject<ResourcesRef>;
let resizeObservers: TestResizeObserver[] = [];

class TestResizeObserver {
  targets = new Set<Element>();

  constructor(public callback: ResizeObserverCallback) {
    resizeObservers.push(this);
  }

  observe(target: Element) {
    this.targets.add(target);
  }

  unobserve(target: Element) {
    this.targets.delete(target);
  }

  disconnect() {
    this.targets.clear();
  }

  trigger() {
    this.callback(
      [...this.targets].map((target) => ({ target }) as ResizeObserverEntry),
      this as unknown as ResizeObserver,
    );
  }
}

const naturalHeight = (element: HTMLElement) => {
  const card = element.matches("[data-card-height]")
    ? element
    : element.querySelector<HTMLElement>("[data-card-height]");

  return card ? Number(card.dataset.cardHeight) : viewportHeight;
};

const flushFrames = () => {
  for (let pass = 0; frames.size > 0 && pass < 10; pass++) {
    const due = [...frames.values()];

    frames.clear();
    act(() => due.forEach((callback) => callback(pass * 16)));
  }
};

const renderCards = (heights: number[], columnCount = 2) => {
  act(() => {
    root.render(
      <Resources
        ref={resourcesRef}
        cellCount={heights.length}
        columnCount={columnCount}
        renderCell={({ rowIndex, columnIndex, style }) => {
          const index = rowIndex * columnCount + columnIndex;

          return (
            <div data-cell-index={index} style={style}>
              <div data-card-height={heights[index]} />
            </div>
          );
        }}
      />,
    );
  });
  flushFrames();
};

const cell = (index: number) =>
  container.querySelector<HTMLElement>(`[data-cell-index="${index}"]`)!;

const top = (index: number) => Number.parseFloat(cell(index).style.top);

const resize = () => {
  act(() => resizeObservers.forEach((observer) => observer.trigger()));
  flushFrames();
};

beforeEach(() => {
  vi.stubGlobal("IS_REACT_ACT_ENVIRONMENT", true);
  viewportWidth = 600;
  hidden = false;
  frames = new Map();
  resizeObservers = [];
  nextFrameId = 1;
  vi.stubGlobal("ResizeObserver", TestResizeObserver);
  vi.stubGlobal("requestAnimationFrame", (callback: FrameRequestCallback) => {
    const id = nextFrameId++;

    frames.set(id, callback);

    return id;
  });
  vi.stubGlobal("cancelAnimationFrame", (id: number) => frames.delete(id));
  vi.spyOn(HTMLElement.prototype, "offsetWidth", "get").mockImplementation(function (
    this: HTMLElement,
  ) {
    if (hidden) return 0;

    return Number.parseFloat(this.style.width) || viewportWidth;
  });
  vi.spyOn(HTMLElement.prototype, "clientWidth", "get").mockImplementation(function (
    this: HTMLElement,
  ) {
    if (hidden) return 0;

    return Number.parseFloat(this.style.width) || viewportWidth;
  });
  vi.spyOn(HTMLElement.prototype, "offsetHeight", "get").mockImplementation(function (
    this: HTMLElement,
  ) {
    if (hidden) return 0;

    return Number.parseFloat(this.style.height) || naturalHeight(this);
  });
  vi.spyOn(HTMLElement.prototype, "clientHeight", "get").mockImplementation(function (
    this: HTMLElement,
  ) {
    if (hidden) return 0;

    return Number.parseFloat(this.style.height) || viewportHeight;
  });
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
  resourcesRef = React.createRef<ResourcesRef>();
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe("resource grid measurements", () => {
  it("settles scrollbar presence when measured rows fit but estimated rows overflow", () => {
    vi.spyOn(Grid.defaultProps, "getScrollbarSize").mockReturnValue(15);

    expect(() => renderCards([120, 120, 120, 120, 120, 120, 120, 120])).not.toThrow();
    resize();

    expect(top(6)).toBe(360);
  });

  it("restores an overflowing grid after its tab is hidden without a scrollbar update loop", () => {
    vi.spyOn(Grid.defaultProps, "getScrollbarSize").mockReturnValue(15);
    renderCards(Array(50).fill(200), 8);
    resize();

    expect(() => {
      hidden = true;
      resize();
      renderCards(Array(50).fill(220), 8);
    }).not.toThrow();

    hidden = false;
    resize();

    expect(top(8)).toBe(220);
  });

  it("moves later rows when detail content grows after the initial cards were measured", () => {
    renderCards([120, 140, 120, 120, 120, 120]);
    resize();
    expect(top(2)).toBe(140);

    // Mirrors Phase 2: the same resource cards gain their display name/tags.
    renderCards([240, 160, 120, 120, 120, 120]);
    act(() => resourcesRef.current?.measure());
    flushFrames();

    expect(top(2)).toBe(240);
    expect(top(4)).toBe(360);
  });

  it("rebuilds cached row offsets after the layout is rearranged", () => {
    renderCards([120, 140, 120, 120, 120, 120]);
    resize();
    expect(top(2)).toBe(140);

    renderCards([200, 180, 120, 120, 120, 120]);
    act(() => resourcesRef.current?.rearrange());
    flushFrames();

    expect(top(2)).toBe(200);
    expect(top(4)).toBe(320);
  });

  it("remeasures wrapped content when the container width changes", () => {
    renderCards([120, 140, 120, 120, 120, 120]);
    resize();
    expect(top(2)).toBe(140);

    viewportWidth = 400;
    // A narrower card makes its name wrap onto additional lines.
    renderCards([220, 180, 120, 120, 120, 120]);
    resize();

    expect(cell(0).style.width).toBe("200px");
    expect(top(2)).toBe(220);
  });

  it("uses the tallest new row when the column count changes", () => {
    renderCards([120, 140, 240, 120, 120, 120]);
    resize();
    expect(top(2)).toBe(140);

    renderCards([120, 140, 240, 120, 120, 120], 3);

    expect(top(3)).toBe(240);
  });

  it("restores accurate measurements after an inactive tab becomes visible", () => {
    renderCards([120, 140, 120, 120, 120, 120]);
    resize();

    hidden = true;
    resize();
    renderCards([240, 160, 120, 120, 120, 120]);
    act(() => resourcesRef.current?.measure());
    flushFrames();

    hidden = false;
    resize();

    expect(top(2)).toBe(240);
    expect(top(4)).toBe(360);
  });
});
