import { useEffect, useRef, useState } from "react";

/**
 * The canvas viewport: pan (drag empty space, or plain wheel / trackpad two-finger) and zoom
 * (ctrl/⌘+wheel — which is also what trackpad pinch sends — or the corner toolbar), plus a
 * fit-view that centers the chain. The world layer is moved with a CSS transform and the dot
 * grid follows via background-size/position, so the node drag's magnetic hit-testing — which
 * works in screen coordinates through getBoundingClientRect — needs no changes at any zoom.
 *
 * Pan and zoom write through a ref and apply styles imperatively: a pointermove/wheel stream
 * must not re-render the whole editor. Only the toolbar's percent label goes through state.
 */

const MIN_SCALE = 0.35;
const MAX_SCALE = 2;
const GRID = 22;
const FIT_PADDING = 96;
const FIT_MIN_EDGE = 24;
/** Fit never shrinks below this — node labels must stay readable. A chain that still
 * doesn't fit anchors to its start (left) with the vertical centering kept; panning
 * covers the rest. */
const FIT_MIN_SCALE = 0.75;
const PAN_START = 3;

/** Things a pan must never start from — everything interactive keeps its own gestures. */
const NO_PAN_SELECTOR =
  '[role="button"],button,input,select,textarea,[data-remove-zone],[aria-haspopup]';

export interface CanvasViewApi {
  /** The transformed world layer wrapping the chain. */
  worldRef: React.MutableRefObject<HTMLDivElement | null>;
  zoomPct: number;
  zoomIn: () => void;
  zoomOut: () => void;
  resetZoom: () => void;
  /** Center the chain in the canvas (scaling down when it doesn't fit). */
  fit: () => void;
  onPanPointerDown: (ev: React.PointerEvent) => void;
}

export function useCanvasView(
  canvasRef: React.MutableRefObject<HTMLDivElement | null>,
  ready: boolean,
): CanvasViewApi {
  const worldRef = useRef<HTMLDivElement | null>(null);
  const view = useRef({ x: 0, y: 0, scale: 1 });
  const [zoomPct, setZoomPct] = useState(100);
  const didInitialFit = useRef(false);

  const apply = () => {
    const canvas = canvasRef.current;
    const world = worldRef.current;

    if (!canvas || !world) return;
    const v = view.current;

    world.style.transform = `translate(${v.x}px, ${v.y}px) scale(${v.scale})`;
    canvas.style.backgroundSize = `${GRID * v.scale}px ${GRID * v.scale}px`;
    canvas.style.backgroundPosition = `${v.x}px ${v.y}px`;
  };

  /** Rescale around a canvas-local origin so the point under the cursor stays put. */
  const scaleAround = (nextScale: number, originX: number, originY: number) => {
    const v = view.current;
    const clamped = Math.min(MAX_SCALE, Math.max(MIN_SCALE, nextScale));
    const k = clamped / v.scale;

    v.x = originX - (originX - v.x) * k;
    v.y = originY - (originY - v.y) * k;
    v.scale = clamped;
    apply();
    setZoomPct(Math.round(clamped * 100));
  };

  const scaleAtCenter = (factor: number) => {
    const canvas = canvasRef.current;

    if (!canvas) return;
    scaleAround(view.current.scale * factor, canvas.clientWidth / 2, canvas.clientHeight / 2);
  };

  const fit = () => {
    const canvas = canvasRef.current;
    const world = worldRef.current;

    if (!canvas || !world) return;
    const w = world.offsetWidth;
    const h = world.offsetHeight;
    const cw = canvas.clientWidth;
    const ch = canvas.clientHeight;

    if (!w || !h || !cw || !ch) return;
    const scale = Math.max(
      FIT_MIN_SCALE,
      Math.min((cw - FIT_PADDING) / w, (ch - FIT_PADDING) / h, 1),
    );
    const v = view.current;

    v.scale = scale;
    // Centered both ways; a chain wider than the view anchors left so it reads start-first.
    v.x = Math.max((cw - w * scale) / 2, FIT_MIN_EDGE);
    v.y = Math.max((ch - h * scale) / 2, FIT_MIN_EDGE);
    apply();
    setZoomPct(Math.round(scale * 100));
  };

  // Initial fit, once the real canvas exists and the chain has laid out.
  useEffect(() => {
    if (!ready || didInitialFit.current) return;
    didInitialFit.current = true;
    requestAnimationFrame(() => fit());
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [ready]);

  // Wheel: ctrl/⌘ (and trackpad pinch) zooms to the cursor; plain wheel pans. Native
  // listener with passive:false — the canvas owns the wheel entirely.
  useEffect(() => {
    if (!ready) return;
    const canvas = canvasRef.current;

    if (!canvas) return;
    const onWheel = (e: WheelEvent) => {
      e.preventDefault();
      const v = view.current;

      if (e.ctrlKey || e.metaKey) {
        const rect = canvas.getBoundingClientRect();

        scaleAround(
          v.scale * Math.exp(-e.deltaY * 0.0015),
          e.clientX - rect.left,
          e.clientY - rect.top,
        );
      } else {
        v.x -= e.deltaX;
        v.y -= e.deltaY;
        apply();
      }
    };

    canvas.addEventListener("wheel", onWheel, { passive: false });

    return () => canvas.removeEventListener("wheel", onWheel);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [ready]);

  const onPanPointerDown = (ev: React.PointerEvent) => {
    if (ev.button !== 0 && ev.pointerType === "mouse") return;
    if ((ev.target as HTMLElement).closest(NO_PAN_SELECTOR)) return;
    const canvas = canvasRef.current;

    if (!canvas) return;
    const startX = ev.clientX;
    const startY = ev.clientY;
    const fromX = view.current.x;
    const fromY = view.current.y;
    let panning = false;

    const onMove = (e: PointerEvent) => {
      const dx = e.clientX - startX;
      const dy = e.clientY - startY;

      if (!panning) {
        if (Math.hypot(dx, dy) < PAN_START) return;
        panning = true;
        canvas.style.cursor = "grabbing";
      }
      view.current.x = fromX + dx;
      view.current.y = fromY + dy;
      apply();
    };
    const onEnd = () => {
      window.removeEventListener("pointermove", onMove);
      window.removeEventListener("pointerup", onEnd);
      window.removeEventListener("pointercancel", onEnd);
      canvas.style.cursor = "";
    };

    window.addEventListener("pointermove", onMove);
    window.addEventListener("pointerup", onEnd);
    window.addEventListener("pointercancel", onEnd);
  };

  return {
    worldRef,
    zoomPct,
    zoomIn: () => scaleAtCenter(1.2),
    zoomOut: () => scaleAtCenter(1 / 1.2),
    resetZoom: () => scaleAtCenter(1 / view.current.scale),
    fit,
    onPanPointerDown,
  };
}
