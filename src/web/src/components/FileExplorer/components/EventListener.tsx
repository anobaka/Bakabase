"use client";

import { useCallback, useEffect, useRef } from "react";

import {
  isDeleteShortcut,
  isPrimaryModifierPressed,
  shouldIgnoreGlobalShortcut,
} from "@/core/keyboard";

export enum SelectionMode {
  Normal = 1,
  Ctrl = 2,
  Shift = 3,
}

type Props = {
  onSelectionModeChange: (mode: SelectionMode) => void;
  onClick?: (evt: MouseEvent) => void;
  onDelete?: () => any;
  onKeyDown?: (key: string, evt: KeyboardEvent) => any;
};
const EventListener = (props: Props) => {
  const propsRef = useRef(props);
  const selectionModeRef = useRef<SelectionMode>(SelectionMode.Normal);

  // Keep propsRef in sync with latest props
  useEffect(() => {
    propsRef.current = props;
  });

  useEffect(() => {
    // Check if we're in browser environment
    if (typeof window === "undefined") {
      return;
    }

    window.addEventListener("keydown", onKeyDown);
    window.addEventListener("keyup", onKeyUp);
    window.addEventListener("click", onClick);
    window.addEventListener("mousedown", updateSelectionMode, true);
    window.addEventListener("blur", resetSelectionMode);
    document.addEventListener("visibilitychange", resetSelectionMode);

    return () => {
      window.removeEventListener("keydown", onKeyDown);
      window.removeEventListener("keyup", onKeyUp);
      window.removeEventListener("click", onClick);
      window.removeEventListener("mousedown", updateSelectionMode, true);
      window.removeEventListener("blur", resetSelectionMode);
      document.removeEventListener("visibilitychange", resetSelectionMode);
    };
  }, []);

  const changeSelectionMode = (mode: SelectionMode) => {
    if (selectionModeRef.current != mode) {
      selectionModeRef.current = mode;
      propsRef.current.onSelectionModeChange(mode);
    }
  };

  const onClick = useCallback((evt: MouseEvent) => {
    propsRef.current.onClick?.(evt);
  }, []);

  const updateSelectionMode = useCallback((event: KeyboardEvent | MouseEvent) => {
    changeSelectionMode(
      event.shiftKey
        ? SelectionMode.Shift
        : isPrimaryModifierPressed(event)
          ? SelectionMode.Ctrl
          : SelectionMode.Normal,
    );
  }, []);

  const resetSelectionMode = useCallback(() => changeSelectionMode(SelectionMode.Normal), []);

  const onKeyDown = useCallback((e: KeyboardEvent) => {
    updateSelectionMode(e);
    if (shouldIgnoreGlobalShortcut(e)) return;
    if (["Control", "Meta", "Shift", "Alt"].includes(e.key)) return;
    // Holding a key may navigate, but must not open repeated dialogs or start jobs twice.
    if (e.repeat && e.key !== "ArrowUp" && e.key !== "ArrowDown") return;
    if (isDeleteShortcut(e)) {
      if (propsRef.current.onDelete?.()) e.preventDefault();
    } else {
      propsRef.current.onKeyDown?.(e.key, e);
    }
  }, []);

  const onKeyUp = useCallback((e: KeyboardEvent) => {
    updateSelectionMode(e);
  }, []);

  return null;
};

EventListener.displayName = "EventListener";

export default EventListener;
