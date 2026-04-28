import { useCallback, useRef, useState, useEffect } from 'react';
import type { DockedEdge } from '../types';
import {
  BAKA_SIZE,
  DOCKED_VISIBLE,
  EDGE_THRESHOLD,
  DRAG_THRESHOLD,
  POSITION_STORAGE_KEY,
  DEFAULT_DOCKED_EDGE,
} from '../constants';

interface Position {
  x: number;
  y: number;
}

export function detectNearEdge(x: number, y: number): DockedEdge | null {
  const w = window.innerWidth;
  const h = window.innerHeight;
  if (x <= EDGE_THRESHOLD) return 'left';
  if (x >= w - BAKA_SIZE - EDGE_THRESHOLD) return 'right';
  if (y <= EDGE_THRESHOLD) return 'top';
  if (y >= h - BAKA_SIZE - EDGE_THRESHOLD) return 'bottom';
  return null;
}

export function getDockedPosition(edge: DockedEdge, current?: Position): Position {
  const w = window.innerWidth;
  const h = window.innerHeight;
  const hiddenPart = BAKA_SIZE - DOCKED_VISIBLE;
  // Clamp the free axis to viewport bounds
  const cy = current ? Math.max(0, Math.min(current.y, h - BAKA_SIZE)) : h - BAKA_SIZE - 80;
  const cx = current ? Math.max(0, Math.min(current.x, w - BAKA_SIZE)) : w / 2 - BAKA_SIZE / 2;
  switch (edge) {
    case 'left': return { x: -hiddenPart, y: cy };
    case 'right': return { x: w - DOCKED_VISIBLE, y: cy };
    case 'top': return { x: cx, y: -hiddenPart };
    case 'bottom': return { x: cx, y: h - DOCKED_VISIBLE };
  }
}

export function loadPosition(): { position: Position; edge: DockedEdge } {
  try {
    const saved = localStorage.getItem(POSITION_STORAGE_KEY);
    if (saved) {
      const parsed = JSON.parse(saved);
      if (parsed.edge) {
        // Restore the free-axis coordinate saved alongside the edge
        const hint: Position | undefined = parsed.freeAxis != null
          ? (parsed.edge === 'left' || parsed.edge === 'right'
            ? { x: 0, y: parsed.freeAxis }
            : { x: parsed.freeAxis, y: 0 })
          : undefined;
        return { position: getDockedPosition(parsed.edge, hint), edge: parsed.edge };
      }
      return { position: parsed.position, edge: DEFAULT_DOCKED_EDGE };
    }
  } catch { /* ignore */ }
  return { position: getDockedPosition(DEFAULT_DOCKED_EDGE), edge: DEFAULT_DOCKED_EDGE };
}

export function useBakaDraggable(
  onDragStart: () => void,
  onDragEnd: (nearEdge: DockedEdge | null) => void,
) {
  const saved = loadPosition();
  const [position, setPosition] = useState<Position>(saved.position);
  const isDraggingRef = useRef(false);
  const [isDraggingState, setIsDraggingState] = useState(false);
  const dragStartRef = useRef<{ mx: number; my: number; px: number; py: number } | null>(null);
  const positionRef = useRef(position);

  useEffect(() => {
    positionRef.current = position;
  }, [position]);

  // Keep position in bounds on resize
  useEffect(() => {
    const handleResize = () => {
      setPosition(prev => ({
        x: Math.min(prev.x, window.innerWidth - BAKA_SIZE),
        y: Math.min(prev.y, window.innerHeight - BAKA_SIZE),
      }));
    };
    window.addEventListener('resize', handleResize);
    return () => window.removeEventListener('resize', handleResize);
  }, []);

  const handleMouseDown = useCallback((e: React.MouseEvent) => {
    e.preventDefault();
    dragStartRef.current = {
      mx: e.clientX,
      my: e.clientY,
      px: positionRef.current.x,
      py: positionRef.current.y,
    };
    isDraggingRef.current = false;

    const handleMouseMove = (me: MouseEvent) => {
      if (!dragStartRef.current) return;
      const dx = me.clientX - dragStartRef.current.mx;
      const dy = me.clientY - dragStartRef.current.my;

      if (!isDraggingRef.current && (Math.abs(dx) > DRAG_THRESHOLD || Math.abs(dy) > DRAG_THRESHOLD)) {
        isDraggingRef.current = true;
        setIsDraggingState(true);
        onDragStart();
      }

      if (isDraggingRef.current) {
        const newX = Math.max(-BAKA_SIZE + DOCKED_VISIBLE, Math.min(dragStartRef.current.px + dx, window.innerWidth - DOCKED_VISIBLE));
        const newY = Math.max(-BAKA_SIZE + DOCKED_VISIBLE, Math.min(dragStartRef.current.py + dy, window.innerHeight - DOCKED_VISIBLE));
        setPosition({ x: newX, y: newY });
      }
    };

    const handleMouseUp = () => {
      document.removeEventListener('mousemove', handleMouseMove);
      document.removeEventListener('mouseup', handleMouseUp);

      if (isDraggingRef.current) {
        const pos = positionRef.current;
        const edge = detectNearEdge(pos.x, pos.y);

        if (edge) {
          const dockedPos = getDockedPosition(edge, pos);
          setPosition(dockedPos);
          try { localStorage.setItem(POSITION_STORAGE_KEY, JSON.stringify({ edge, freeAxis: edge === 'left' || edge === 'right' ? pos.y : pos.x })); } catch { /* */ }
        } else {
          try { localStorage.setItem(POSITION_STORAGE_KEY, JSON.stringify({ position: pos })); } catch { /* */ }
        }

        onDragEnd(edge);
        setIsDraggingState(false);
        setTimeout(() => { isDraggingRef.current = false; }, 100);
      }

      dragStartRef.current = null;
    };

    document.addEventListener('mousemove', handleMouseMove);
    document.addEventListener('mouseup', handleMouseUp);
  }, [onDragStart, onDragEnd]);

  const snapToEdge = useCallback((edge: DockedEdge) => {
    const pos = getDockedPosition(edge, positionRef.current);
    setPosition(pos);
    const freeAxis = edge === 'left' || edge === 'right' ? positionRef.current.y : positionRef.current.x;
    try { localStorage.setItem(POSITION_STORAGE_KEY, JSON.stringify({ edge, freeAxis })); } catch { /* */ }
  }, []);

  // Slide out from a docked edge so the full character is visible
  const undockFromEdge = useCallback((edge: DockedEdge) => {
    const cur = positionRef.current;
    const margin = 4; // small gap from edge
    switch (edge) {
      case 'left':   setPosition({ x: margin, y: cur.y }); break;
      case 'right':  setPosition({ x: window.innerWidth - BAKA_SIZE - margin, y: cur.y }); break;
      case 'top':    setPosition({ x: cur.x, y: margin }); break;
      case 'bottom': setPosition({ x: cur.x, y: window.innerHeight - BAKA_SIZE - margin }); break;
    }
  }, []);

  // Slide back into the docked edge position
  const redockToEdge = useCallback((edge: DockedEdge) => {
    const pos = getDockedPosition(edge, positionRef.current);
    setPosition(pos);
  }, []);

  const isDragging = useCallback(() => isDraggingRef.current, []);

  return { position, setPosition, handleMouseDown, isDragging, isDraggingState, snapToEdge, undockFromEdge, redockToEdge };
}
