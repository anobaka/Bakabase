import { useCallback, useRef, useState, useEffect } from "react";
import { POSITION_STORAGE_KEY, DEFAULT_POSITION } from "../constants";

interface Position {
  x: number;
  y: number;
}

const DRAG_THRESHOLD = 8; // pixels - increased from 5 for less sensitivity

const getInitialPosition = (): Position => {
  if (typeof window === "undefined") return DEFAULT_POSITION;
  try {
    const saved = localStorage.getItem(POSITION_STORAGE_KEY);
    if (saved) {
      const parsed = JSON.parse(saved);
      // Ensure position is within viewport bounds
      const maxX = window.innerWidth - 48;
      const maxY = window.innerHeight - 48;
      return {
        x: Math.max(0, Math.min(parsed.x ?? DEFAULT_POSITION.x, maxX)),
        y: Math.max(0, Math.min(parsed.y ?? DEFAULT_POSITION.y, maxY)),
      };
    }
  } catch {
    // Ignore parse errors
  }
  return DEFAULT_POSITION;
};

export function useDraggable() {
  const [position, setPosition] = useState<Position>(getInitialPosition);
  const [isDraggingState, setIsDraggingState] = useState(false);
  const isDraggingRef = useRef(false);
  const dragStartRef = useRef<{ x: number; y: number; posX: number; posY: number } | null>(null);

  // Update cursor style when dragging
  useEffect(() => {
    if (isDraggingState) {
      document.body.style.cursor = "grabbing";
      document.body.style.userSelect = "none";
    } else {
      document.body.style.cursor = "";
      document.body.style.userSelect = "";
    }
    return () => {
      document.body.style.cursor = "";
      document.body.style.userSelect = "";
    };
  }, [isDraggingState]);

  const handleMouseDown = useCallback((e: React.MouseEvent) => {
    e.preventDefault();
    dragStartRef.current = {
      x: e.clientX,
      y: e.clientY,
      posX: position.x,
      posY: position.y,
    };
    isDraggingRef.current = false;

    const handleMouseMove = (moveEvent: MouseEvent) => {
      if (!dragStartRef.current) return;
      const dx = moveEvent.clientX - dragStartRef.current.x;
      const dy = moveEvent.clientY - dragStartRef.current.y;

      // Mark as dragging if moved more than threshold
      if (!isDraggingRef.current && (Math.abs(dx) > DRAG_THRESHOLD || Math.abs(dy) > DRAG_THRESHOLD)) {
        isDraggingRef.current = true;
        setIsDraggingState(true);
      }

      if (isDraggingRef.current) {
        const newX = Math.max(0, Math.min(dragStartRef.current.posX + dx, window.innerWidth - 48));
        const newY = Math.max(0, Math.min(dragStartRef.current.posY + dy, window.innerHeight - 48));
        setPosition({ x: newX, y: newY });
      }
    };

    const handleMouseUp = () => {
      document.removeEventListener("mousemove", handleMouseMove);
      document.removeEventListener("mouseup", handleMouseUp);

      if (isDraggingRef.current) {
        // Save position to localStorage
        try {
          localStorage.setItem(POSITION_STORAGE_KEY, JSON.stringify(position));
        } catch {
          // Ignore storage errors
        }
      }
      dragStartRef.current = null;

      // Delay resetting drag flag so click/onOpenChange can check it first
      if (isDraggingRef.current) {
        setIsDraggingState(false);
        setTimeout(() => {
          isDraggingRef.current = false;
        }, 100);
      }
    };

    document.addEventListener("mousemove", handleMouseMove);
    document.addEventListener("mouseup", handleMouseUp);
  }, [position]);

  const isDragging = useCallback(() => isDraggingRef.current, []);

  return {
    position,
    handleMouseDown,
    isDragging,
    isDraggingState,
  };
}
