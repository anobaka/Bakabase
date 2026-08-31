import type { RectSelectionEnd, RectSelectionMode } from "../useRectSelection";

import * as React from "react";
import { createRoot } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { useRectSelection } from "../useRectSelection";

/** React 18.3 exports `act`, but this project pins @types/react to 18.0, which predates it. */
const act = (React as unknown as { act: (scope: () => void) => void }).act;

/**
 * Covers the press choreography rather than the arithmetic (see collectRectIndices.test).
 * What matters here is that an ordinary click survives untouched, that the rectangle only
 * appears once the pointer commits to a drag, and that the click the browser delivers
 * afterwards is always accounted for.
 *
 * jsdom has no layout, so the grid's boxes and scroll metrics are stubbed by hand, and
 * animation frames are driven explicitly.
 */

const GridWidth = 400;
const GridHeight = 200;

const stubBox = (
  element: HTMLElement,
  left: number,
  top: number,
  width: number,
  height: number,
) => {
  element.getBoundingClientRect = () =>
    ({
      left,
      top,
      right: left + width,
      bottom: top + height,
      width,
      height,
      x: left,
      y: top,
      toJSON: () => ({}),
    }) as DOMRect;
};

const stubMetric = (element: HTMLElement, name: string, value: number) => {
  Object.defineProperty(element, name, { value, writable: true, configurable: true });
};

let frames = new Map<number, FrameRequestCallback>();
let nextFrameId = 1;

const flushFrame = () => {
  const due = [...frames.values()];

  frames = new Map();
  due.forEach((callback) => callback(0));
};

const mouse = (type: string, init: MouseEventInit) =>
  new MouseEvent(type, { bubbles: true, cancelable: true, button: 0, ...init });

type Harness = {
  container: HTMLDivElement;
  scroller: HTMLDivElement;
  overlay: HTMLDivElement;
  onStart: ReturnType<typeof vi.fn>;
  onChange: ReturnType<typeof vi.fn>;
  onEnd: ReturnType<typeof vi.fn>;
  onActiveChange: ReturnType<typeof vi.fn>;
  onSuppressClick: ReturnType<typeof vi.fn>;
  unmount: () => void;
};

const setup = (): Harness => {
  const container = document.createElement("div");
  const scroller = document.createElement("div");
  const overlay = document.createElement("div");

  scroller.className = "ReactVirtualized__Grid";
  container.appendChild(scroller);
  document.body.appendChild(container);

  stubBox(container, 0, 0, GridWidth, GridHeight);
  stubBox(scroller, 0, 0, GridWidth, GridHeight);
  stubMetric(scroller, "clientWidth", GridWidth);
  stubMetric(scroller, "clientHeight", GridHeight);
  stubMetric(scroller, "scrollHeight", 1000);
  stubMetric(scroller, "scrollTop", 0);
  stubMetric(scroller, "scrollLeft", 0);

  const onStart = vi.fn();
  const onChange = vi.fn();
  const onEnd = vi.fn();
  const onActiveChange = vi.fn();
  const onSuppressClick = vi.fn();

  // 2 columns x 200px, rows 100px tall, 6 cells.
  const Probe = () => {
    const overlayRef = useRectSelection({
      containerRef: { current: container },
      cellCount: 6,
      columnCount: 2,
      columnWidth: 200,
      getRowHeight: () => 100,
      cellInset: 0,
      onStart,
      onChange,
      onEnd,
      onActiveChange,
      onSuppressClick,
    });

    overlayRef.current = overlay;

    return null;
  };

  const host = document.createElement("div");

  document.body.appendChild(host);
  const root = createRoot(host);

  act(() => {
    root.render(React.createElement(Probe));
  });

  return {
    container,
    scroller,
    overlay,
    onStart,
    onChange,
    onEnd,
    onActiveChange,
    onSuppressClick,
    unmount: () =>
      act(() => {
        root.unmount();
      }),
  };
};

/** Press, then travel far enough to commit, then paint one frame. */
const beginDrag = (h: Harness, target: HTMLElement = h.scroller, init: MouseEventInit = {}) => {
  target.dispatchEvent(mouse("mousedown", { clientX: 10, clientY: 10, buttons: 1, ...init }));
  window.dispatchEvent(mouse("mousemove", { clientX: 150, clientY: 50, buttons: 1, ...init }));
  flushFrame();
};

describe("useRectSelection", () => {
  beforeEach(() => {
    (globalThis as Record<string, any>).IS_REACT_ACT_ENVIRONMENT = true;
    frames = new Map();
    nextFrameId = 1;
    vi.stubGlobal("requestAnimationFrame", (callback: FrameRequestCallback) => {
      const id = nextFrameId++;

      frames.set(id, callback);

      return id;
    });
    vi.stubGlobal("cancelAnimationFrame", (id: number) => {
      frames.delete(id);
    });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    document.body.innerHTML = "";
  });

  it("leaves an ordinary click alone", () => {
    const h = setup();

    h.scroller.dispatchEvent(mouse("mousedown", { clientX: 10, clientY: 10, buttons: 1 }));
    window.dispatchEvent(mouse("mousemove", { clientX: 12, clientY: 13, buttons: 1 }));
    flushFrame();
    window.dispatchEvent(mouse("mouseup", { clientX: 12, clientY: 13, buttons: 0 }));

    expect(h.onStart).not.toHaveBeenCalled();
    expect(h.onChange).not.toHaveBeenCalled();
    expect(h.onEnd).not.toHaveBeenCalled();
    expect(h.onSuppressClick).not.toHaveBeenCalled();

    const click = mouse("click", { clientX: 12, clientY: 13 });

    h.scroller.dispatchEvent(click);
    expect(click.defaultPrevented).toBe(false);

    h.unmount();
  });

  it("selects what the rectangle covers once the pointer commits", () => {
    const h = setup();

    beginDrag(h);

    expect(h.onStart).toHaveBeenCalledTimes(1);
    expect(h.onActiveChange).toHaveBeenCalledWith(true);
    // (10,10)-(150,50) touches the first column of the first row only.
    expect(h.onChange).toHaveBeenLastCalledWith([0], "replace");
    expect(h.overlay.style.display).toBe("block");
    expect(h.overlay.style.width).toBe("140px");

    window.dispatchEvent(mouse("mousemove", { clientX: 260, clientY: 160, buttons: 1 }));
    flushFrame();
    expect(h.onChange).toHaveBeenLastCalledWith([0, 1, 2, 3], "replace");

    h.unmount();
  });

  it("swallows the click that closes the drag", () => {
    const h = setup();

    beginDrag(h);
    window.dispatchEvent(mouse("mouseup", { clientX: 150, clientY: 50, buttons: 0 }));

    expect(h.onEnd).toHaveBeenCalledWith<[RectSelectionEnd]>({ cancelled: false });
    expect(h.onActiveChange).toHaveBeenLastCalledWith(false);
    expect(h.onSuppressClick).toHaveBeenCalledTimes(1);
    expect(h.overlay.style.display).toBe("");

    const click = mouse("click", { clientX: 150, clientY: 50 });

    h.scroller.dispatchEvent(click);
    expect(click.defaultPrevented).toBe(true);

    h.unmount();
  });

  it("reads the modifier live, so the combining rule can change mid-drag", () => {
    const h = setup();

    beginDrag(h);
    expect(h.onChange).toHaveBeenLastCalledWith<[number[], RectSelectionMode]>([0], "replace");

    window.dispatchEvent(
      mouse("mousemove", { clientX: 151, clientY: 50, buttons: 1, altKey: true }),
    );
    flushFrame();
    expect(h.onChange).toHaveBeenLastCalledWith<[number[], RectSelectionMode]>([0], "subtract");

    window.dispatchEvent(
      mouse("mousemove", { clientX: 152, clientY: 50, buttons: 1, ctrlKey: true }),
    );
    flushFrame();
    expect(h.onChange).toHaveBeenLastCalledWith<[number[], RectSelectionMode]>([0], "append");

    h.unmount();
  });

  it("never starts on a control that owns the press itself", () => {
    const h = setup();
    const button = document.createElement("button");

    h.scroller.appendChild(button);
    beginDrag(h, button);

    expect(h.onStart).not.toHaveBeenCalled();
    expect(h.onChange).not.toHaveBeenCalled();

    h.unmount();
  });

  it("leaves selectable text to text selection", () => {
    const h = setup();
    // A card's title and tag rows carry .select-text; dragging there must highlight
    // text rather than start a rectangle.
    const title = document.createElement("div");
    const label = document.createElement("span");

    title.className = "select-text resource-limited-content";
    title.appendChild(label);
    h.scroller.appendChild(title);
    beginDrag(h, label);

    expect(h.onStart).not.toHaveBeenCalled();
    expect(h.onChange).not.toHaveBeenCalled();

    h.unmount();
  });

  it("keeps going when a live rectangle sweeps over selectable text", () => {
    const h = setup();
    // Excluding .select-text must only gate where a drag *starts*. Sweeping across a
    // card's title or tag row mid-drag has to keep selecting, not hand the gesture
    // back to text selection.
    const title = document.createElement("div");
    const label = document.createElement("span");

    title.className = "select-text resource-limited-content";
    title.appendChild(label);
    h.scroller.appendChild(title);

    beginDrag(h);
    expect(h.onChange).toHaveBeenLastCalledWith([0], "replace");

    label.dispatchEvent(mouse("mousemove", { clientX: 260, clientY: 160, buttons: 1 }));
    flushFrame();
    expect(h.onChange).toHaveBeenLastCalledWith([0, 1, 2, 3], "replace");

    label.dispatchEvent(mouse("mouseup", { clientX: 260, clientY: 160, buttons: 0 }));
    expect(h.onEnd).toHaveBeenCalledWith<[RectSelectionEnd]>({ cancelled: false });

    h.unmount();
  });

  it("ignores a press that is not the primary button", () => {
    const h = setup();

    h.scroller.dispatchEvent(
      mouse("mousedown", { clientX: 10, clientY: 10, button: 2, buttons: 2 }),
    );
    window.dispatchEvent(mouse("mousemove", { clientX: 150, clientY: 50, buttons: 2 }));
    flushFrame();

    expect(h.onStart).not.toHaveBeenCalled();

    h.unmount();
  });

  it("drops the rectangle on Escape but still accounts for the click on release", () => {
    const h = setup();

    beginDrag(h);
    h.onChange.mockClear();

    window.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true }));

    expect(h.onEnd).toHaveBeenCalledWith<[RectSelectionEnd]>({ cancelled: true });
    expect(h.overlay.style.display).toBe("");

    // The button is still down; further movement must not revive the rectangle.
    window.dispatchEvent(mouse("mousemove", { clientX: 300, clientY: 180, buttons: 1 }));
    flushFrame();
    expect(h.onChange).not.toHaveBeenCalled();

    window.dispatchEvent(mouse("mouseup", { clientX: 300, clientY: 180, buttons: 0 }));
    expect(h.onEnd).toHaveBeenCalledTimes(1);
    expect(h.onSuppressClick).toHaveBeenCalledTimes(1);

    const click = mouse("click", { clientX: 300, clientY: 180 });

    h.scroller.dispatchEvent(click);
    expect(click.defaultPrevented).toBe(true);

    h.unmount();
  });

  it("scrolls the grid when the pointer reaches the bottom edge", () => {
    const h = setup();

    beginDrag(h);
    window.dispatchEvent(mouse("mousemove", { clientX: 150, clientY: 195, buttons: 1 }));
    flushFrame();

    expect(h.scroller.scrollTop).toBeGreaterThan(0);

    const scrolled = h.scroller.scrollTop;

    flushFrame();
    expect(h.scroller.scrollTop).toBeGreaterThan(scrolled);

    h.unmount();
  });

  it("gives up when the button turns out to have been released off-window", () => {
    const h = setup();

    beginDrag(h);
    window.dispatchEvent(mouse("mousemove", { clientX: 200, clientY: 60, buttons: 0 }));

    expect(h.onEnd).toHaveBeenCalledWith<[RectSelectionEnd]>({ cancelled: false });
    expect(h.onActiveChange).toHaveBeenLastCalledWith(false);
    expect(h.overlay.style.display).toBe("");

    h.unmount();
  });

  it("stops listening once unmounted", () => {
    const h = setup();

    h.unmount();
    beginDrag(h);

    expect(h.onStart).not.toHaveBeenCalled();
  });
});
