"use client";

import { useCallback, useEffect, useRef } from "react";
import { useTranslation } from "react-i18next";

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
  const { t } = useTranslation();
  const propsRef = useRef(props);
  const selectionModeRef = useRef<SelectionMode>(SelectionMode.Normal);
  const shiftHoldingRef = useRef(false);

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

    return () => {
      window.removeEventListener("keydown", onKeyDown);
      window.removeEventListener("keyup", onKeyUp);
      window.removeEventListener("click", onClick);
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

  const onKeyDown = useCallback((e: KeyboardEvent) => {
    switch (e.key) {
      case "Control":
      case "Meta": // Support Command key on Mac
        changeSelectionMode(SelectionMode.Ctrl);
        break;
      case "Shift":
        changeSelectionMode(SelectionMode.Shift);
        shiftHoldingRef.current = true;
        break;
      case "Delete":
        propsRef.current.onDelete?.();
        break;
      default:
        propsRef.current.onKeyDown?.(e.key, e);
        break;
    }
  }, []);

  const onKeyUp = useCallback((e: KeyboardEvent) => {
    switch (e.key) {
      case "Control":
      case "Meta": // Support Command key on Mac
        changeSelectionMode(SelectionMode.Normal);
        break;
      case "Shift":
        changeSelectionMode(SelectionMode.Normal);
        shiftHoldingRef.current = false;
        break;
    }
  }, []);

  return null;
};

EventListener.displayName = "EventListener";

export default EventListener;
