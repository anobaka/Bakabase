"use client";

import type { ComponentProps, ReactNode } from "react";

import { useRef, useState } from "react";
import { ControlledMenu } from "@szhsin/react-menu";
import { useTranslation } from "react-i18next";

import ContextMenuItems from "../ContextMenuItems";

import "./index.css";

type Props = ComponentProps<typeof ContextMenuItems> & {
  children: ReactNode;
  disabled?: boolean;
};

const firstItem = { position: "first" as const };

const ResourceContextMenu = ({ children, disabled, ...items }: Props) => {
  const { t } = useTranslation();
  const triggerRef = useRef<HTMLDivElement>(null);
  const [open, setOpen] = useState(false);
  // Preserve lazy mounting: most cards never have their menu opened.
  const [everOpened, setEverOpened] = useState(false);
  const [keyboard, setKeyboard] = useState(false);
  const [anchorPoint, setAnchorPoint] = useState({ x: 0, y: 0 });

  const openMenu = (point: { x: number; y: number }, fromKeyboard: boolean) => {
    if (disabled) return;
    setAnchorPoint(point);
    setKeyboard(fromKeyboard);
    setEverOpened(true);
    setOpen(true);
  };

  const openFromKeyboard = () => {
    const bounds = triggerRef.current!.getBoundingClientRect();

    openMenu({ x: bounds.left + 12, y: bounds.top + 12 }, true);
  };

  return (
    // A focusable card group keeps its nested links/buttons accessible; a button role would hide them.
    // eslint-disable-next-line jsx-a11y/no-noninteractive-element-interactions
    <div
      ref={triggerRef}
      aria-keyshortcuts="Shift+F10"
      aria-label={t("resource.contextMenu.trigger", {
        name: items.contextResource?.displayName || t("resource.contextMenu.title"),
      })}
      className="resource-context-menu-trigger"
      role="group"
      tabIndex={disabled ? -1 : 0}
      onContextMenu={(event) => {
        if (typeof document.hasFocus === "function" && !document.hasFocus()) return;
        event.preventDefault();
        event.stopPropagation();
        if (event.clientX === 0 && event.clientY === 0) openFromKeyboard();
        else openMenu({ x: event.clientX, y: event.clientY }, false);
      }}
      onKeyDown={(event) => {
        if (event.key !== "ContextMenu" && !(event.shiftKey && event.key === "F10")) return;
        event.preventDefault();
        event.stopPropagation();
        openFromKeyboard();
      }}
    >
      {children}
      <ControlledMenu
        portal
        anchorPoint={anchorPoint}
        boundingBoxPadding="8"
        direction="right"
        menuClassName="resource-context-menu"
        menuItemFocus={keyboard ? firstItem : undefined}
        overflow="auto"
        state={open ? "open" : "closed"}
        submenuCloseDelay={180}
        submenuOpenDelay={120}
        viewScroll="close"
        onClick={(event) => {
          event.preventDefault();
          event.stopPropagation();
        }}
        onClose={(event) => {
          setOpen(false);
          if (event.key === "Escape") triggerRef.current?.focus({ preventScroll: true });
        }}
      >
        {everOpened && <ContextMenuItems {...items} />}
      </ControlledMenu>
    </div>
  );
};

export default ResourceContextMenu;
