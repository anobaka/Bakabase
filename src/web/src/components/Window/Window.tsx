"use client";

import type { WindowProps, WindowState } from "./types";

import React, { useCallback, useEffect, useRef, useState } from "react";
import { Rnd } from "react-rnd";

import { WindowManager } from "./WindowManager";
import { WindowHeader } from "./WindowHeader";
import "./styles.css";

const Window: React.FC<WindowProps> = (props) => {
  const { title, children, onClose, afterClose, onDestroyed, windowOptions, renderHeaderActions } = props;

  const windowIdRef = useRef<string>(`window-${Date.now()}-${Math.random()}`);
  const windowManager = WindowManager.getInstance();
  const rndRef = useRef<any>(null);

  const [windowState, setWindowState] = useState<WindowState>(() => {
    const initialState = windowManager.registerWindow(windowIdRef.current, {
      x: windowOptions?.initialPosition?.x ?? 100,
      y: windowOptions?.initialPosition?.y ?? 100,
      width: windowOptions?.initialSize?.width ?? 1000,
      height: windowOptions?.initialSize?.height ?? 700,
      isMinimized: false,
      isMaximized: false,
    });

    return initialState;
  });

  const [restoreState, setRestoreState] = useState<{
    x: number;
    y: number;
    width: number;
    height: number;
  } | null>(null);

  const [isClosed, setIsClosed] = useState(false);
  const isDraggingRef = useRef(false);
  const dragPositionRef = useRef<{ x: number; y: number } | null>(null);
  const isResizingRef = useRef(false);
  const resizeStateRef = useRef<{ x: number; y: number; width: number; height: number } | null>(null);
  const optimisticUpdateRef = useRef<Partial<WindowState> | null>(null);

  useEffect(() => {
    // Update window state with viewport dimensions
    const updateViewport = () => {
      windowManager.updateWindow(windowIdRef.current, {
        innerWidth: window.innerWidth,
        innerHeight: window.innerHeight,
      });
    };

    updateViewport();
    window.addEventListener("resize", updateViewport);

    return () => {
      window.removeEventListener("resize", updateViewport);
      windowManager.unregisterWindow(windowIdRef.current);
    };
  }, []);

  useEffect(() => {
    // Subscribe to window updates
    const unsubscribe = windowManager.subscribe((windows) => {
      const currentWindow = windows.find((w) => w.id === windowIdRef.current);

      if (currentWindow) {
        // Use resize state if available (during/after resize stop)
        if (resizeStateRef.current) {
          const resizeState = resizeStateRef.current;

          resizeStateRef.current = null; // Clear after use
          setWindowState({
            ...currentWindow,
            x: resizeState.x,
            y: resizeState.y,
            width: resizeState.width,
            height: resizeState.height,
          });
        }
        // Use drag position if available (during/after drag stop)
        else if (dragPositionRef.current) {
          const dragPos = dragPositionRef.current;

          dragPositionRef.current = null; // Clear after use
          setWindowState({
            ...currentWindow,
            x: dragPos.x,
            y: dragPos.y,
          });
        } else {
          // If we have an optimistic update, merge it with the WindowManager state
          // to ensure our optimistic update isn't lost
          if (optimisticUpdateRef.current) {
            const optimistic = optimisticUpdateRef.current;
            optimisticUpdateRef.current = null; // Clear after use
            setWindowState((prev) => {
              // Merge optimistic updates with WindowManager state
              const merged = {
                ...currentWindow,
                // Preserve optimistic updates for critical state fields
                ...(optimistic.isMinimized !== undefined ? { isMinimized: optimistic.isMinimized } : {}),
                ...(optimistic.isMaximized !== undefined ? { isMaximized: optimistic.isMaximized } : {}),
                ...(optimistic.x !== undefined ? { x: optimistic.x } : {}),
                ...(optimistic.y !== undefined ? { y: optimistic.y } : {}),
                ...(optimistic.width !== undefined ? { width: optimistic.width } : {}),
                ...(optimistic.height !== undefined ? { height: optimistic.height } : {}),
              };
              return merged;
            });
          } else {
            setWindowState(currentWindow);
          }
        }
      }
    });

    return unsubscribe;
  }, []);

  const handleDragStart = useCallback(() => {
    isDraggingRef.current = true;
    dragPositionRef.current = null;
    windowManager.bringToFront(windowIdRef.current);
  }, []);

  const handleDrag = useCallback(
    (_e: any, d: { x: number; y: number }) => {
      if (windowState.isMaximized) return;
    },
    [windowState.isMaximized],
  );

  const handleDragStop = useCallback(
    (_e: any, d: { x: number; y: number }) => {
      if (windowState.isMaximized) {
        isDraggingRef.current = false;

        return;
      }

      const snapped = windowManager.snapPosition(
        windowIdRef.current,
        d.x,
        d.y,
        windowState.width,
        windowState.height,
      );

      // Store the final position in ref to prevent reset during subscription update
      dragPositionRef.current = { x: snapped.x, y: snapped.y };

      // Update WindowManager (this will trigger subscription update)
      windowManager.updateWindow(windowIdRef.current, {
        x: snapped.x,
        y: snapped.y,
      });

      // Clear dragging flag
      isDraggingRef.current = false;
    },
    [windowState.isMaximized, windowState.width, windowState.height],
  );

  const handleResizeStart = useCallback(() => {
    isResizingRef.current = true;
    resizeStateRef.current = null;
    windowManager.bringToFront(windowIdRef.current);
  }, []);

  const handleResize = useCallback(
    (
      _e: any,
      _direction: any,
      ref: HTMLElement,
      _delta: { width: number; height: number },
      position: { x: number; y: number },
    ) => {
      if (windowState.isMaximized) return;
    },
    [windowState.isMaximized],
  );

  const handleResizeStop = useCallback(
    (
      _e: any,
      _direction: any,
      ref: HTMLElement,
      _delta: { width: number; height: number },
      position: { x: number; y: number },
    ) => {
      if (windowState.isMaximized) {
        isResizingRef.current = false;

        return;
      }

      const width = ref.offsetWidth;
      const height = ref.offsetHeight;

      const snappedSize = windowManager.snapSize(
        windowIdRef.current,
        position.x,
        position.y,
        width,
        height,
      );

      const snappedPos = windowManager.snapPosition(
        windowIdRef.current,
        position.x,
        position.y,
        snappedSize.width,
        snappedSize.height,
      );

      // Store the final state in ref to prevent reset during subscription update
      resizeStateRef.current = {
        x: snappedPos.x,
        y: snappedPos.y,
        width: snappedSize.width,
        height: snappedSize.height,
      };

      // Update WindowManager (this will trigger subscription update)
      windowManager.updateWindow(windowIdRef.current, {
        x: snappedPos.x,
        y: snappedPos.y,
        width: snappedSize.width,
        height: snappedSize.height,
      });

      // Clear resizing flag
      isResizingRef.current = false;
    },
    [windowState.isMaximized],
  );

  const handleMinimize = useCallback(() => {
    const newIsMinimized = !windowState.isMinimized;
    // Track optimistic update
    optimisticUpdateRef.current = { isMinimized: newIsMinimized };
    // Update local state immediately for instant UI feedback
    setWindowState((prev) => ({
      ...prev,
      isMinimized: newIsMinimized,
    }));
    // Update WindowManager for consistency
    windowManager.updateWindow(windowIdRef.current, {
      isMinimized: newIsMinimized,
    });
  }, [windowState.isMinimized]);

  const handleMaximize = useCallback(() => {
    if (windowState.isMaximized) {
      // Restore
      if (restoreState) {
        // Track optimistic update
        optimisticUpdateRef.current = {
          isMaximized: false,
          x: restoreState.x,
          y: restoreState.y,
          width: restoreState.width,
          height: restoreState.height,
        };
        // Update local state immediately for instant UI feedback
        setWindowState((prev) => ({
          ...prev,
          isMaximized: false,
          x: restoreState.x,
          y: restoreState.y,
          width: restoreState.width,
          height: restoreState.height,
        }));
        windowManager.updateWindow(windowIdRef.current, {
          isMaximized: false,
          x: restoreState.x,
          y: restoreState.y,
          width: restoreState.width,
          height: restoreState.height,
        });
        setRestoreState(null);
      }
    } else {
      // Maximize
      const newRestoreState = {
        x: windowState.x,
        y: windowState.y,
        width: windowState.width,
        height: windowState.height,
      };
      setRestoreState(newRestoreState);
      // Track optimistic update
      optimisticUpdateRef.current = {
        isMaximized: true,
        x: 0,
        y: 0,
        width: window.innerWidth,
        height: window.innerHeight,
      };
      // Update local state immediately for instant UI feedback
      setWindowState((prev) => ({
        ...prev,
        isMaximized: true,
        x: 0,
        y: 0,
        width: window.innerWidth,
        height: window.innerHeight,
      }));
      windowManager.updateWindow(windowIdRef.current, {
        isMaximized: true,
        x: 0,
        y: 0,
        width: window.innerWidth,
        height: window.innerHeight,
      });
    }
  }, [windowState.isMaximized, windowState, restoreState]);

  const handleClose = useCallback(
    (e?: React.MouseEvent) => {
      e?.stopPropagation();
      // Set closed state to stop rendering immediately
      setIsClosed(true);
      // Manually close RND by clearing the ref and unregistering from window manager
      if (rndRef.current) {
        rndRef.current = null;
      }
      windowManager.unregisterWindow(windowIdRef.current);
      if (afterClose) {
        afterClose();
      }
      // Call onDestroyed to trigger portal unmount
      if (onDestroyed) {
        onDestroyed();
      }
      // Call onClose if provided
      if (onClose) {
        onClose();
      }
    },
    [afterClose, onDestroyed, onClose],
  );

  const handleMouseDown = useCallback(() => {
    windowManager.bringToFront(windowIdRef.current);
  }, []);

  // Keyboard event handler - only handle events if this window is topmost
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      // Only handle keyboard events if this window is topmost
      if (!windowManager.isTopmost(windowIdRef.current)) {
        return;
      }

      if (e.key === "Escape") {
        handleClose();
        e.preventDefault();
        e.stopPropagation();
      }
    };

    document.addEventListener("keydown", handleKeyDown, true);

    return () => {
      document.removeEventListener("keydown", handleKeyDown, true);
    };
  }, [handleClose]);

  // Tailwind classes for the window container
  const windowClasses =
    "window-component rounded-lg overflow-hidden shadow-[0_10px_40px_rgba(0,0,0,0.5),0_0_0_1px_rgba(255,255,255,0.1)] bg-[rgba(20,20,20,0.95)] backdrop-blur-[10px] flex flex-col";

  // Return null if closed to stop rendering immediately
  if (isClosed) {
    return null;
  }

  if (windowState.isMinimized) {
    return (
      <Rnd
        ref={rndRef}
        className={windowClasses}
        dragHandleClassName="window-header"
        maxHeight={40}
        maxWidth={300}
        minHeight={40}
        minWidth={300}
        position={{ x: windowState.x, y: windowState.y }}
        size={{ width: 300, height: 40 }}
        style={{ zIndex: windowState.zIndex }}
        onDrag={handleDrag}
        onDragStart={handleDragStart}
        onDragStop={handleDragStop}
        onMouseDown={handleMouseDown}
      >
        <WindowHeader
          isMinimized={true}
          windowState={windowState}
          onClose={handleClose}
          onMaximize={handleMaximize}
          onMinimize={handleMinimize}
          title={title}
          renderActions={renderHeaderActions}
        />
      </Rnd>
    );
  }

  return (
    <Rnd
      ref={rndRef}
      bounds="window"
      className={windowClasses}
      disableResizing={windowState.isMaximized}
      dragHandleClassName="window-header"
      minHeight={windowOptions?.minHeight ?? 300}
      minWidth={windowOptions?.minWidth ?? 400}
      position={{ x: windowState.x, y: windowState.y }}
      size={{
        width: windowState.width,
        height: windowState.height,
      }}
      style={{ zIndex: windowState.zIndex }}
      onDrag={handleDrag}
      onDragStart={handleDragStart}
      onDragStop={handleDragStop}
      onMouseDown={handleMouseDown}
      onResize={handleResize}
      onResizeStart={handleResizeStart}
      onResizeStop={handleResizeStop}
    >
      <div className="flex flex-col w-full h-full overflow-hidden">
        <WindowHeader
          windowState={windowState}
          onClose={handleClose}
          onMaximize={handleMaximize}
          onMinimize={handleMinimize}
          title={title}
          renderActions={renderHeaderActions}
        />
        <div className="flex-1 overflow-hidden">
          {children}
        </div>
      </div>
    </Rnd>
  );
};

export default Window;
