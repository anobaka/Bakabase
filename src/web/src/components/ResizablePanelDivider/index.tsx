"use client";

import React, { useCallback, useEffect, useRef, useState } from "react";
import { LeftOutlined, RightOutlined } from "@ant-design/icons";

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
  /** Enable collapse functionality */
  collapsible?: boolean;
  /** Storage key for persisting collapsed state */
  collapsedStorageKey?: string;
  /** Callback when collapsed state changes */
  onCollapsedChange?: (collapsed: boolean) => void;
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
  collapsible = false,
  collapsedStorageKey,
  onCollapsedChange,
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

  const [collapsed, setCollapsed] = useState<boolean>(() => {
    if (collapsedStorageKey && typeof window !== "undefined") {
      const stored = localStorage.getItem(collapsedStorageKey);
      return stored === "true";
    }
    return false;
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

  const toggleCollapse = useCallback(() => {
    const newCollapsed = !collapsed;
    setCollapsed(newCollapsed);

    // Save to localStorage if collapsedStorageKey is provided
    if (collapsedStorageKey) {
      localStorage.setItem(collapsedStorageKey, newCollapsed.toString());
    }

    onCollapsedChange?.(newCollapsed);
  }, [collapsed, collapsedStorageKey, onCollapsedChange]);

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
        className={`flex-shrink-0 h-full overflow-hidden transition-all duration-300 ${collapsed ? "w-0" : ""}`}
        style={{ width: collapsed ? 0 : panelWidth }}
      >
        {!collapsed && leftPanel}
      </div>

      {/* Divider with collapse button */}
      <div className="flex-shrink-0 relative">
        {/* Draggable divider bar */}
        {!collapsed && (
          <div
            className="w-1 h-full cursor-col-resize hover:bg-primary/30 active:bg-primary/50 transition-colors group"
            onMouseDown={handleMouseDown}
          >
            {/* Visual indicator on hover */}
            <div className="absolute inset-y-0 -left-1 -right-1 group-hover:bg-primary/10" />
          </div>
        )}

        {/* Collapse/Expand button */}
        {collapsible && (
          <button
            type="button"
            className={`absolute top-1/2 -translate-y-1/2 z-10 w-5 h-10 flex items-center justify-center bg-default-100 hover:bg-default-200 border border-default-300 rounded-r-md transition-colors ${collapsed ? "left-0" : "right-0"}`}
            onClick={toggleCollapse}
            title={collapsed ? "Expand panel" : "Collapse panel"}
          >
            {collapsed ? (
              <RightOutlined className="text-xs text-default-600" />
            ) : (
              <LeftOutlined className="text-xs text-default-600" />
            )}
          </button>
        )}
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
