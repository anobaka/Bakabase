"use client";

import React, { useCallback, useEffect, useRef, useState } from "react";

interface Props {
  /** Default width in pixels */
  defaultWidth: number;
  /** Minimum width in pixels */
  minWidth?: number;
  /** Maximum width in pixels */
  maxWidth?: number;
  /** Callback when width changes */
  onWidthChange?: (width: number) => void;
  /** Left panel content */
  leftPanel: React.ReactNode;
  /** Right panel content */
  rightPanel: React.ReactNode;
  /** Additional class name for the container */
  className?: string;
  /** Storage key for persisting width */
  storageKey?: string;
}

/**
 * A horizontally resizable panel layout with a draggable divider.
 * Renders two panels side by side with a draggable divider between them.
 */
const ResizablePanelDivider: React.FC<Props> = ({
  defaultWidth,
  minWidth = 200,
  maxWidth = 600,
  onWidthChange,
  leftPanel,
  rightPanel,
  className = "",
  storageKey,
}) => {
  const [panelWidth, setPanelWidth] = useState<number>(() => {
    if (storageKey && typeof window !== "undefined") {
      const stored = localStorage.getItem(storageKey);
      if (stored) {
        const parsed = parseInt(stored, 10);
        if (!isNaN(parsed) && parsed >= minWidth && parsed <= maxWidth) {
          return parsed;
        }
      }
    }
    return defaultWidth;
  });

  const isDragging = useRef(false);
  const startX = useRef(0);
  const startWidth = useRef(0);
  const containerRef = useRef<HTMLDivElement>(null);

  const handleMouseDown = useCallback((e: React.MouseEvent) => {
    e.preventDefault();
    isDragging.current = true;
    startX.current = e.clientX;
    startWidth.current = panelWidth;
    document.body.style.cursor = "col-resize";
    document.body.style.userSelect = "none";
  }, [panelWidth]);

  const handleMouseMove = useCallback((e: MouseEvent) => {
    if (!isDragging.current) return;

    const delta = e.clientX - startX.current;
    let newWidth = startWidth.current + delta;

    // Clamp to min/max
    newWidth = Math.max(minWidth, Math.min(maxWidth, newWidth));

    setPanelWidth(newWidth);
  }, [minWidth, maxWidth]);

  const handleMouseUp = useCallback(() => {
    if (!isDragging.current) return;

    isDragging.current = false;
    document.body.style.cursor = "";
    document.body.style.userSelect = "";

    // Save to localStorage if storageKey is provided
    if (storageKey) {
      localStorage.setItem(storageKey, panelWidth.toString());
    }

    onWidthChange?.(panelWidth);
  }, [panelWidth, onWidthChange, storageKey]);

  useEffect(() => {
    document.addEventListener("mousemove", handleMouseMove);
    document.addEventListener("mouseup", handleMouseUp);

    return () => {
      document.removeEventListener("mousemove", handleMouseMove);
      document.removeEventListener("mouseup", handleMouseUp);
    };
  }, [handleMouseMove, handleMouseUp]);

  return (
    <div ref={containerRef} className={`flex flex-row h-full ${className}`}>
      {/* Left Panel */}
      <div
        className="flex-shrink-0 h-full overflow-hidden"
        style={{ width: panelWidth }}
      >
        {leftPanel}
      </div>

      {/* Divider */}
      <div
        className="flex-shrink-0 w-1 cursor-col-resize hover:bg-primary/30 active:bg-primary/50 transition-colors relative group"
        onMouseDown={handleMouseDown}
      >
        {/* Visual indicator on hover */}
        <div className="absolute inset-y-0 -left-1 -right-1 group-hover:bg-primary/10" />
      </div>

      {/* Right Panel */}
      <div className="flex-grow min-w-0 h-full overflow-hidden">
        {rightPanel}
      </div>
    </div>
  );
};

ResizablePanelDivider.displayName = "ResizablePanelDivider";

export default ResizablePanelDivider;
