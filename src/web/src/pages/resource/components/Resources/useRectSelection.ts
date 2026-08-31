"use client";

import type React from "react";

import { useEffect, useRef } from "react";

/** How the rectangle combines with the selection that existed when the drag started. */
export type RectSelectionMode = "replace" | "append" | "subtract";

export type RectSelectionEnd = {
  /** The drag was aborted (Escape, right click, window blur) instead of released. */
  cancelled: boolean;
};

/** Everything needed to place a cell, mirroring what the `Grid` itself was given. */
export type RectSelectionGeometry = {
  cellCount: number;
  columnCount: number;
  /** Width of one column, i.e. the exact value handed to `<Grid columnWidth>`. */
  columnWidth: number;
  /** Height of a row, i.e. the exact function handed to `<Grid rowHeight>`. */
  getRowHeight: (index: number) => number;
  /** Padding the caller's `renderCell` bakes into every cell; ignored while hit-testing. */
  cellInset: number;
};

/** A rectangle in the grid's scrolled content space. */
export type ContentRect = {
  left: number;
  top: number;
  right: number;
  bottom: number;
};

/**
 * Indices of every cell whose box overlaps `rect`.
 *
 * Derived from the grid's geometry rather than from rendered DOM nodes, so cells
 * virtualization has not mounted — everything the rectangle covers after an
 * auto-scroll — are found just the same.
 */
export const collectRectIndices = (
  { cellCount, columnCount, columnWidth, getRowHeight, cellInset }: RectSelectionGeometry,
  { left, top, right, bottom }: ContentRect,
): number[] => {
  const indices: number[] = [];

  if (cellCount <= 0 || columnCount <= 0 || !Number.isFinite(columnWidth) || columnWidth <= 0) {
    return indices;
  }

  const rowCount = Math.ceil(cellCount / columnCount);
  let rowTop = 0;

  for (let row = 0; row < rowCount && rowTop <= bottom; row++) {
    const rowBottom = rowTop + getRowHeight(row);

    if (rowBottom - cellInset > top && rowTop + cellInset < bottom) {
      for (let column = 0; column < columnCount; column++) {
        const index = row * columnCount + column;

        if (index >= cellCount) {
          break;
        }
        const cellLeft = column * columnWidth;

        if (cellLeft + columnWidth - cellInset > left && cellLeft + cellInset < right) {
          indices.push(index);
        }
      }
    }
    rowTop = rowBottom;
  }

  return indices;
};

type Options = RectSelectionGeometry & {
  /** Wrapper hosting the virtualized grid. Doubles as the positioning context for the overlay. */
  containerRef: React.MutableRefObject<HTMLDivElement | null>;
  onStart?: () => void;
  onChange?: (indices: number[], mode: RectSelectionMode) => void;
  onEnd?: (result: RectSelectionEnd) => void;
  /** Fires when the rectangle appears/disappears so the grid can mute hover effects. */
  onActiveChange?: (active: boolean) => void;
  /** Fires the moment a click has to be ignored, i.e. right before the browser delivers
   *  the one that closes the drag. Consumers with their own document-level click
   *  handlers have to opt out of it themselves — this hook cannot outrun a listener
   *  that was registered before it. */
  onSuppressClick?: () => void;
};

/** Pointer travel before a press becomes a drag. Small enough to feel instant, large
 *  enough that a sloppy click still opens the resource it landed on. */
const DragThreshold = 6;
/** Distance from the viewport edge at which the grid starts scrolling itself. */
const AutoScrollEdge = 48;
/** Auto-scroll speed bounds, in px per animation frame. */
const AutoScrollMinSpeed = 2;
const AutoScrollMaxSpeed = 24;

/** Elements that own the press themselves, so a rectangle must never start on them.
 *  Anchors are matched with or without an href: a link built on react-aria fires its
 *  press from a global pointerup, which swallowing the trailing click would not stop.
 *  `[role=button]` is deliberately absent — the resource cover carries it and spans
 *  most of every card, which would leave almost nowhere left to drag from. */
const InteractiveSelector = [
  "button",
  "a",
  "input",
  "textarea",
  "select",
  "[role='link']",
  "[contenteditable='true']",
  "[role='dialog']",
  "[role='menu']",
  ".szh-menu",
].join(",");

type Modifiers = {
  altKey: boolean;
  ctrlKey: boolean;
  metaKey: boolean;
  shiftKey: boolean;
};

const readMode = (e: Modifiers): RectSelectionMode => {
  if (e.altKey) {
    return "subtract";
  }
  if (e.ctrlKey || e.metaKey || e.shiftKey) {
    return "append";
  }

  return "replace";
};

const sameIndices = (a: number[], b: number[]) => {
  if (a.length !== b.length) {
    return false;
  }
  for (let i = 0; i < a.length; i++) {
    if (a[i] !== b[i]) {
      return false;
    }
  }

  return true;
};

const speedFor = (depth: number) =>
  Math.min(
    AutoScrollMaxSpeed,
    Math.max(AutoScrollMinSpeed, (depth / AutoScrollEdge) * AutoScrollMaxSpeed),
  );

type DragState = {
  scroller: HTMLElement;
  /** Drag origin in the grid's scrolled content space, so auto-scrolling cannot move it. */
  anchorX: number;
  anchorY: number;
  /** Where the press started, in viewport space, for the drag threshold. */
  originX: number;
  originY: number;
  /** Latest pointer position, in viewport space. */
  pointerX: number;
  pointerY: number;
  mode: RectSelectionMode;
  /** The pointer travelled far enough for the rectangle to appear. */
  active: boolean;
  /** Aborted, but the button is still down and its click still has to be swallowed. */
  cancelled: boolean;
  emittedIndices: number[];
  emittedMode: RectSelectionMode | null;
};

/**
 * Drag-a-rectangle multi-selection for a react-virtualized `Grid`.
 *
 * Hit-testing runs on the grid's own geometry (column width + the row heights the
 * Grid was given) rather than on rendered DOM nodes, so cells that virtualization
 * has not mounted — everything the rectangle covers after an auto-scroll — are
 * still selected.
 */
export const useRectSelection = (options: Options) => {
  const optionsRef = useRef(options);
  const overlayRef = useRef<HTMLDivElement | null>(null);
  const dragRef = useRef<DragState | null>(null);
  const frameRef = useRef(0);
  // The wrapper is assigned by a ref callback that re-renders on assignment, so reading
  // it here is stable — and keying the effect on the element means the listener follows
  // the wrapper if it is ever remounted.
  const container = options.containerRef.current;

  optionsRef.current = options;

  useEffect(() => {
    if (!container) {
      return;
    }

    const update = (drag: DragState) => {
      const gridRect = drag.scroller.getBoundingClientRect();
      const pointerX = drag.pointerX - gridRect.left + drag.scroller.scrollLeft;
      const pointerY = drag.pointerY - gridRect.top + drag.scroller.scrollTop;
      const left = Math.min(drag.anchorX, pointerX);
      const right = Math.max(drag.anchorX, pointerX);
      const top = Math.min(drag.anchorY, pointerY);
      const bottom = Math.max(drag.anchorY, pointerY);
      const overlay = overlayRef.current;

      if (overlay) {
        const containerRect = container.getBoundingClientRect();
        const offsetX = gridRect.left - containerRect.left - drag.scroller.scrollLeft;
        const offsetY = gridRect.top - containerRect.top - drag.scroller.scrollTop;

        overlay.style.display = "block";
        overlay.style.left = `${left + offsetX}px`;
        overlay.style.top = `${top + offsetY}px`;
        overlay.style.width = `${right - left}px`;
        overlay.style.height = `${bottom - top}px`;
      }

      const indices = collectRectIndices(optionsRef.current, { left, top, right, bottom });

      if (drag.emittedMode !== drag.mode || !sameIndices(indices, drag.emittedIndices)) {
        drag.emittedIndices = indices;
        drag.emittedMode = drag.mode;
        optionsRef.current.onChange?.(indices, drag.mode);
      }
    };

    const step = () => {
      const drag = dragRef.current;

      if (!drag || drag.cancelled) {
        frameRef.current = 0;

        return;
      }
      frameRef.current = requestAnimationFrame(step);

      const gridRect = drag.scroller.getBoundingClientRect();
      const topGap = drag.pointerY - gridRect.top;
      const bottomGap = gridRect.bottom - drag.pointerY;
      let delta = 0;

      if (topGap < AutoScrollEdge) {
        delta = -speedFor(AutoScrollEdge - topGap);
      } else if (bottomGap < AutoScrollEdge) {
        delta = speedFor(AutoScrollEdge - bottomGap);
      }
      if (delta !== 0) {
        const maxScrollTop = drag.scroller.scrollHeight - drag.scroller.clientHeight;

        drag.scroller.scrollTop = Math.max(
          0,
          Math.min(maxScrollTop, drag.scroller.scrollTop + delta),
        );
      }
      update(drag);
    };

    /** Releasing the button still produces a click. Eat it, or it opens whatever card
     *  the pointer happens to be over and clears the selection just made. */
    const swallowNextClick = () => {
      const swallow = (e: MouseEvent) => {
        e.stopPropagation();
        e.preventDefault();
        window.removeEventListener("click", swallow, true);
      };

      window.addEventListener("click", swallow, true);
      // The click arrives in the same task as the mouseup; anything still armed by the
      // time this runs is a click that never came.
      setTimeout(() => window.removeEventListener("click", swallow, true), 0);
      optionsRef.current.onSuppressClick?.();
    };

    const stopFrame = () => {
      if (frameRef.current !== 0) {
        cancelAnimationFrame(frameRef.current);
        frameRef.current = 0;
      }
    };

    const detach = () => {
      window.removeEventListener("mousemove", onMouseMove, true);
      window.removeEventListener("mouseup", onRelease, true);
      window.removeEventListener("keydown", onKeyDown, true);
      window.removeEventListener("keyup", onKeyUp, true);
      window.removeEventListener("contextmenu", onCancel, true);
      window.removeEventListener("blur", onWindowBlur);
      window.removeEventListener("dragstart", onDragStart, true);
    };

    /** Takes the rectangle away without ending the press: the button is still down, and
     *  its eventual click still has to be swallowed. */
    const cancel = () => {
      const drag = dragRef.current;

      if (!drag || drag.cancelled) {
        return;
      }
      drag.cancelled = true;
      stopFrame();
      if (overlayRef.current) {
        overlayRef.current.style.display = "";
      }
      if (drag.active) {
        optionsRef.current.onActiveChange?.(false);
        optionsRef.current.onEnd?.({ cancelled: true });
      }
    };

    /** The press is over — tear everything down. */
    const release = () => {
      const drag = dragRef.current;

      if (!drag) {
        return;
      }
      dragRef.current = null;
      detach();
      stopFrame();
      if (overlayRef.current) {
        overlayRef.current.style.display = "";
      }
      if (!drag.active) {
        return;
      }
      if (!drag.cancelled) {
        optionsRef.current.onActiveChange?.(false);
        optionsRef.current.onEnd?.({ cancelled: false });
      }
      swallowNextClick();
    };

    const onMouseMove = (e: MouseEvent) => {
      const drag = dragRef.current;

      if (!drag) {
        return;
      }
      // The button was let go somewhere we never heard about — released outside the
      // window, or swallowed by a native drag. This is the first move that tells us.
      if ((e.buttons & 1) === 0) {
        release();

        return;
      }
      if (drag.cancelled) {
        return;
      }
      drag.pointerX = e.clientX;
      drag.pointerY = e.clientY;
      drag.mode = readMode(e);

      if (!drag.active) {
        if (
          Math.abs(e.clientX - drag.originX) < DragThreshold &&
          Math.abs(e.clientY - drag.originY) < DragThreshold
        ) {
          return;
        }
        drag.active = true;
        // The first few pixels may already have started a native text selection.
        window.getSelection()?.removeAllRanges();
        optionsRef.current.onStart?.();
        optionsRef.current.onActiveChange?.(true);
      }
      if (frameRef.current === 0) {
        frameRef.current = requestAnimationFrame(step);
      }
    };

    const onRelease = () => release();

    const onCancel = () => cancel();

    const onWindowBlur = () => {
      cancel();
      release();
    };

    /** Covers are images; without this the press would become a native image drag. */
    const onDragStart = (e: Event) => e.preventDefault();

    /** Modifiers are read live, so the user can decide mid-drag whether the rectangle
     *  replaces, extends or shrinks the selection. */
    const onKeyUp = (e: KeyboardEvent) => {
      const drag = dragRef.current;

      if (drag && !drag.cancelled) {
        drag.mode = readMode(e);
      }
    };

    const onKeyDown = (e: KeyboardEvent) => {
      if (!dragRef.current) {
        return;
      }
      if (e.key === "Escape") {
        e.preventDefault();
        e.stopPropagation();
        cancel();

        return;
      }
      onKeyUp(e);
    };

    const onMouseDown = (e: MouseEvent) => {
      if (!optionsRef.current.onChange || e.button !== 0 || e.defaultPrevented || dragRef.current) {
        return;
      }
      const target = e.target as HTMLElement | null;

      if (!target || !container.contains(target) || target.closest(InteractiveSelector)) {
        return;
      }
      const scroller = container.querySelector<HTMLElement>(".ReactVirtualized__Grid");

      if (!scroller) {
        return;
      }
      const gridRect = scroller.getBoundingClientRect();
      const offsetX = e.clientX - gridRect.left;
      const offsetY = e.clientY - gridRect.top;

      // A press on the scrollbar gutter belongs to the scrollbar.
      if (offsetX > scroller.clientWidth || offsetY > scroller.clientHeight) {
        return;
      }

      dragRef.current = {
        scroller,
        anchorX: offsetX + scroller.scrollLeft,
        anchorY: offsetY + scroller.scrollTop,
        originX: e.clientX,
        originY: e.clientY,
        pointerX: e.clientX,
        pointerY: e.clientY,
        mode: readMode(e),
        active: false,
        cancelled: false,
        emittedIndices: [],
        emittedMode: null,
      };

      // The press is left alone until it travels far enough to be a drag, so an ordinary
      // click still focuses, opens and double-click-selects exactly as before.
      window.addEventListener("mousemove", onMouseMove, true);
      window.addEventListener("mouseup", onRelease, true);
      window.addEventListener("keydown", onKeyDown, true);
      window.addEventListener("keyup", onKeyUp, true);
      window.addEventListener("contextmenu", onCancel, true);
      window.addEventListener("blur", onWindowBlur);
      window.addEventListener("dragstart", onDragStart, true);
    };

    container.addEventListener("mousedown", onMouseDown);

    return () => {
      container.removeEventListener("mousedown", onMouseDown);
      dragRef.current = null;
      detach();
      stopFrame();
    };
  }, [container]);

  return overlayRef;
};
