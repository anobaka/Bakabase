import type { SlotFit } from "./types";

import { useCallback, useEffect, useRef, useState } from "react";

/**
 * The canvas drag interaction (design: docs/workflow-editor-redesign.html §3).
 *
 * Click vs drag: nothing visual happens on pointerdown; the ghost, source fade and remove
 * zone only materialize once the pointer travels DRAG_START px, so a plain click stays a
 * click (select the node). Slots participate in the magnetic search only when the dragged
 * kind actually fits there — validity IS the magnetism, so an invalid drop position simply
 * never opens.
 *
 * The ghost follows the pointer imperatively (a cloned element positioned outside React) —
 * only the infrequent state (active slot, remove-zone hover, live flag) goes through React.
 */

const MAGNET_RADIUS = 90;
const DRAG_START = 6;

export interface DragState {
  kind: string;
  /** Chain index the node is dragged from; null when it comes from the palette. */
  fromIdx: number | null;
}

export interface ChainDragApi {
  /** Live drag in progress (past the click threshold). */
  dragging: DragState | null;
  /** Slot index the pointer is snapped to, with how the kind would fit there. */
  activeSlot: { index: number; fit: Exclude<SlotFit, null> } | null;
  overRemove: boolean;
  startPaletteDrag: (ev: React.PointerEvent, kind: string) => void;
  startNodeDrag: (ev: React.PointerEvent, kind: string, fromIdx: number) => void;
  /** The scrollable canvas element — slots and the remove zone are queried inside it. */
  canvasRef: React.MutableRefObject<HTMLDivElement | null>;
}

interface Options {
  getSlotFit: (kind: string, slotIdx: number, fromIdx: number | null) => SlotFit;
  /** slotIdx is in current-chain coordinates; null slot + removed=true means deletion. */
  onDrop: (
    drag: DragState,
    slotIdx: number | null,
    fit: Exclude<SlotFit, null> | null,
    removed: boolean,
  ) => void;
  /** A pointerdown+up on a chain node that never became a drag. */
  onNodeClick: (idx: number) => void;
  /** A pointerdown+up on a palette item that never became a drag — "append to tail". */
  onPaletteClick: (kind: string) => void;
}

export function useChainDrag({
  getSlotFit,
  onDrop,
  onNodeClick,
  onPaletteClick,
}: Options): ChainDragApi {
  const canvasRef = useRef<HTMLDivElement | null>(null);
  const [dragging, setDragging] = useState<DragState | null>(null);
  const [activeSlot, setActiveSlot] = useState<{
    index: number;
    fit: Exclude<SlotFit, null>;
  } | null>(null);
  const [overRemove, setOverRemove] = useState(false);

  // Everything the move handler needs, without re-subscribing listeners per render.
  const session = useRef<{
    kind: string;
    fromIdx: number | null;
    sourceEl: HTMLElement;
    startX: number;
    startY: number;
    live: boolean;
    ghost: HTMLElement | null;
    activeSlot: { index: number; fit: Exclude<SlotFit, null> } | null;
    overRemove: boolean;
  } | null>(null);
  const optionsRef = useRef({ getSlotFit, onDrop, onNodeClick, onPaletteClick });

  optionsRef.current = { getSlotFit, onDrop, onNodeClick, onPaletteClick };

  // The active session's cancel function — lets an unmount mid-drag tear everything down
  // (ghost, window listeners) instead of leaving a frozen card behind.
  const cancelActive = useRef<(() => void) | null>(null);

  useEffect(() => () => cancelActive.current?.(), []);

  const begin = useCallback(
    (ev: React.PointerEvent, kind: string, fromIdx: number | null) => {
      // Left button / primary touch only.
      if (ev.button !== 0 && ev.pointerType === "mouse") return;
      ev.preventDefault();
      session.current = {
        kind,
        fromIdx,
        sourceEl: ev.currentTarget as HTMLElement,
        startX: ev.clientX,
        startY: ev.clientY,
        live: false,
        ghost: null,
        activeSlot: null,
        overRemove: false,
      };

      const onMove = (e: PointerEvent) => {
        const s = session.current;

        if (!s) return;

        if (!s.live) {
          if (Math.hypot(e.clientX - s.startX, e.clientY - s.startY) < DRAG_START) return;
          s.live = true;
          const ghost = s.sourceEl.cloneNode(true) as HTMLElement;

          ghost.dataset.dragGhost = "";
          ghost.style.cssText +=
            ";position:fixed;z-index:60;pointer-events:none;opacity:.92;margin:0;" +
            `width:${s.sourceEl.offsetWidth}px;transform:translate(-50%,-50%) rotate(1.5deg);` +
            "box-shadow:0 10px 28px rgba(0,0,0,.35)";
          document.body.appendChild(ghost);
          s.ghost = ghost;
          setDragging({ kind: s.kind, fromIdx: s.fromIdx });
        }

        if (s.ghost) {
          s.ghost.style.left = `${e.clientX}px`;
          s.ghost.style.top = `${e.clientY}px`;
        }

        // Magnetic search over the slots the dragged kind fits. Assignments go through an
        // object property so TS control-flow narrowing doesn't fight the closure writes.
        const search = {
          best: null as { index: number; fit: Exclude<SlotFit, null> } | null,
          distance: MAGNET_RADIUS,
        };

        canvasRef.current?.querySelectorAll<HTMLElement>("[data-slot]").forEach((el) => {
          const index = parseInt(el.dataset.slot!, 10);

          // Dropping right around the node's own position is a no-op — don't magnetize.
          if (s.fromIdx != null && (index === s.fromIdx || index === s.fromIdx + 1)) return;
          const fit = optionsRef.current.getSlotFit(s.kind, index, s.fromIdx);

          if (!fit) return;
          const r = el.getBoundingClientRect();
          const d = Math.hypot(
            e.clientX - (r.left + r.width / 2),
            e.clientY - (r.top + r.height / 2),
          );

          if (d < search.distance) {
            search.distance = d;
            search.best = { index, fit };
          }
        });
        let best = search.best;

        // The remove zone (existing nodes only) wins over slots when hovered.
        let hotRemove = false;

        if (s.fromIdx != null) {
          const rz = canvasRef.current?.querySelector<HTMLElement>("[data-remove-zone]");

          if (rz) {
            const r = rz.getBoundingClientRect();

            hotRemove =
              e.clientX >= r.left &&
              e.clientX <= r.right &&
              e.clientY >= r.top - 10 &&
              e.clientY <= r.bottom + 10;
          }
        }
        if (hotRemove) best = null;

        const prev = s.activeSlot;

        if (prev?.index !== best?.index || prev?.fit !== best?.fit) {
          s.activeSlot = best;
          setActiveSlot(best);
        }
        if (s.overRemove !== hotRemove) {
          s.overRemove = hotRemove;
          setOverRemove(hotRemove);
        }
      };

      /**
       * The ONE exit for a session — pointerup (commit), pointercancel (the browser took
       * the pointer away: native drag, touch scroll…), or unmount. The ghost is removed
       * from the CAPTURED session object; reading it back through `session.current` after
       * nulling it is exactly the bug that used to leave frozen cards on the canvas.
       */
      const endSession = (commit: boolean) => {
        window.removeEventListener("pointermove", onMove);
        window.removeEventListener("pointerup", onUp);
        window.removeEventListener("pointercancel", onCancel);
        cancelActive.current = null;
        const s = session.current;

        session.current = null;
        s?.ghost?.remove();
        setDragging(null);
        setActiveSlot(null);
        setOverRemove(false);
        if (!s || !commit) return;

        if (!s.live) {
          if (s.fromIdx != null) optionsRef.current.onNodeClick(s.fromIdx);
          else optionsRef.current.onPaletteClick(s.kind);

          return;
        }

        optionsRef.current.onDrop(
          { kind: s.kind, fromIdx: s.fromIdx },
          s.activeSlot?.index ?? null,
          s.activeSlot?.fit ?? null,
          s.overRemove,
        );
      };
      const onUp = () => endSession(true);
      const onCancel = () => endSession(false);

      cancelActive.current = () => endSession(false);
      window.addEventListener("pointermove", onMove);
      window.addEventListener("pointerup", onUp);
      window.addEventListener("pointercancel", onCancel);
    },
    [],
  );

  return {
    dragging,
    activeSlot,
    overRemove,
    canvasRef,
    startPaletteDrag: (ev, kind) => begin(ev, kind, null),
    startNodeDrag: (ev, kind, fromIdx) => begin(ev, kind, fromIdx),
  };
}
