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
 *
 * Architecture: During drag/collapse operations, we use a "placeholder + overlay" pattern:
 * 1. A placeholder div maintains the current layout (prevents right panel re-renders)
 * 2. An overlay panel floats above and handles the animation/drag
 * 3. After operation completes, we remove the overlay and update the actual layout once
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

  // Overlay state: when active, we show a floating panel for drag/animation
  const [overlayState, setOverlayState] = useState<{
    active: boolean;
    width: number;
    placeholderWidth: number;
  } | null>(null);

  const containerRef = useRef<HTMLDivElement>(null);
  const leftPanelRef = useRef<HTMLDivElement>(null);
  const overlayRef = useRef<HTMLDivElement>(null);

  // Drag state refs
  const isDragging = useRef(false);
  const startX = useRef(0);
  const startWidth = useRef(0);

  // Animation state refs
  const isAnimating = useRef(false);
  const targetCollapsed = useRef(collapsed);

  // Start drag operation
  const handleMouseDown = useCallback((e: React.MouseEvent) => {
    e.preventDefault();
    isDragging.current = true;
    startX.current = e.clientX;
    startWidth.current = panelWidth;

    // Activate overlay mode: placeholder keeps current width, overlay handles drag
    setOverlayState({
      active: true,
      width: panelWidth,
      placeholderWidth: panelWidth,
    });

    document.body.style.cursor = "col-resize";
    document.body.style.userSelect = "none";
  }, [panelWidth]);

  // Handle drag movement - only update overlay width via DOM
  const handleMouseMove = useCallback((e: MouseEvent) => {
    if (!isDragging.current || !overlayRef.current) return;

    const delta = e.clientX - startX.current;
    let newWidth = startWidth.current + delta;
    newWidth = Math.max(minWidth, Math.min(maxWidth, newWidth));

    // Only update overlay DOM directly, no React state changes
    overlayRef.current.style.width = `${newWidth}px`;
  }, [minWidth, maxWidth]);

  // End drag operation
  const handleMouseUp = useCallback(() => {
    if (!isDragging.current) return;

    isDragging.current = false;
    document.body.style.cursor = "";
    document.body.style.userSelect = "";

    // Get final width from overlay DOM
    const finalWidth = overlayRef.current
      ? parseInt(overlayRef.current.style.width, 10) || panelWidth
      : panelWidth;

    // Deactivate overlay and update actual width in one go
    setOverlayState(null);
    setPanelWidth(finalWidth);

    if (storageKey) {
      localStorage.setItem(storageKey, finalWidth.toString());
    }
    onWidthChange?.(finalWidth);
  }, [panelWidth, onWidthChange, storageKey]);

  // Toggle collapse with overlay animation
  const toggleCollapse = useCallback(() => {
    if (isAnimating.current) return;

    const newCollapsed = !collapsed;
    targetCollapsed.current = newCollapsed;
    isAnimating.current = true;

    if (collapsedStorageKey) {
      localStorage.setItem(collapsedStorageKey, newCollapsed.toString());
    }

    if (newCollapsed) {
      // Collapsing: placeholder keeps current width, overlay animates to 0
      setOverlayState({
        active: true,
        width: panelWidth,
        placeholderWidth: panelWidth,
      });

      // Start animation after overlay is mounted
      requestAnimationFrame(() => {
        if (overlayRef.current) {
          overlayRef.current.style.width = "0px";
        }
      });
    } else {
      // Expanding: placeholder is 0, overlay animates from 0 to target width
      setOverlayState({
        active: true,
        width: 0,
        placeholderWidth: 0,
      });

      // Start animation after overlay is mounted
      requestAnimationFrame(() => {
        if (overlayRef.current) {
          // Force reflow before animation
          overlayRef.current.offsetHeight;
          overlayRef.current.style.width = `${panelWidth}px`;
        }
      });
    }
  }, [collapsed, panelWidth, collapsedStorageKey]);

  // Handle overlay transition end
  const handleOverlayTransitionEnd = useCallback((e: React.TransitionEvent) => {
    if (e.propertyName !== "width" || !isAnimating.current) return;

    isAnimating.current = false;
    const newCollapsed = targetCollapsed.current;

    // Deactivate overlay and update collapsed state
    setOverlayState(null);
    setCollapsed(newCollapsed);
    onCollapsedChange?.(newCollapsed);
  }, [onCollapsedChange]);

  // Global mouse event listeners
  useEffect(() => {
    document.addEventListener("mousemove", handleMouseMove);
    document.addEventListener("mouseup", handleMouseUp);

    return () => {
      document.removeEventListener("mousemove", handleMouseMove);
      document.removeEventListener("mouseup", handleMouseUp);
    };
  }, [handleMouseMove, handleMouseUp]);

  // Calculate actual left panel width (used when overlay is not active)
  const actualLeftWidth = collapsed ? 0 : panelWidth;

  return (
    <div ref={containerRef} className={`flex flex-row h-full ${className}`}>
      {/* Left Panel (or Placeholder when overlay is active) */}
      <div
        ref={leftPanelRef}
        className="flex-shrink-0 h-full overflow-hidden"
        style={{
          width: overlayState ? overlayState.placeholderWidth : actualLeftWidth,
          // No transition on placeholder - it stays fixed
        }}
      >
        {/* Only render content when not collapsed and overlay is not active */}
        {!collapsed && !overlayState && leftPanel}
      </div>

      {/* Divider with collapse button */}
      <div className="flex-shrink-0 relative">
        {/* Draggable divider bar - hide when collapsed and no overlay */}
        {(!collapsed || overlayState) && (
          <div
            className="w-1 h-full cursor-col-resize hover:bg-primary/30 active:bg-primary/50 transition-colors group"
            onMouseDown={handleMouseDown}
          >
            <div className="absolute inset-y-0 -left-1 -right-1 group-hover:bg-primary/10" />
          </div>
        )}

        {/* Collapse/Expand button */}
        {collapsible && (
          <button
            type="button"
            className={`absolute top-1/2 -translate-y-1/2 z-10 w-5 h-10 flex items-center justify-center bg-default-100 hover:bg-default-200 border border-default-300 rounded-r-md transition-colors ${collapsed && !overlayState ? "left-0" : "right-0"}`}
            onClick={toggleCollapse}
            title={collapsed ? "Expand panel" : "Collapse panel"}
          >
            {collapsed && !overlayState ? (
              <RightOutlined className="text-xs text-default-600" />
            ) : (
              <LeftOutlined className="text-xs text-default-600" />
            )}
          </button>
        )}
      </div>

      {/* Right Panel - always uses flex-grow, unaffected by overlay */}
      <div className="flex-grow min-w-0 h-full overflow-hidden">
        {rightPanel}
      </div>

      {/* Overlay Panel - floats above during drag/animation */}
      {overlayState?.active && (
        <div
          ref={overlayRef}
          className="absolute top-0 left-0 h-full overflow-hidden bg-background z-20 transition-[width] duration-300"
          style={{
            width: overlayState.width,
            // Disable transition during drag
            transitionDuration: isDragging.current ? "0ms" : "300ms",
          }}
          onTransitionEnd={handleOverlayTransitionEnd}
        >
          {leftPanel}
        </div>
      )}
    </div>
  );
};

ResizablePanelDivider.displayName = "ResizablePanelDivider";

export default ResizablePanelDivider;
