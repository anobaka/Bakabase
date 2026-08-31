"use client";

import React, { useMemo } from "react";
import { useTranslation } from "react-i18next";
import { RiKeyboardLine } from "react-icons/ri";

import { Button, Kbd, Modal, Tooltip } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

interface Shortcut {
  labelKey: string;
  /** Modifier key names (Ctrl/⌘/Shift/Alt) stay as-is; the action words are localized. */
  keys: (modifierKey: string, click: string, drag: string, rightClick: string) => string;
}

const shortcuts: Shortcut[] = [
  {
    labelKey: "resource.shortcut.selectMultiple",
    keys: (mod, click) => `${mod} + ${click}`,
  },
  {
    labelKey: "resource.shortcut.selectRange",
    keys: (_, click) => `Shift + ${click}`,
  },
  {
    labelKey: "resource.shortcut.rectSelect",
    keys: (_, __, drag) => drag,
  },
  {
    labelKey: "resource.shortcut.rectSelectAppend",
    keys: (mod, _, drag) => `${mod} + ${drag}`,
  },
  {
    labelKey: "resource.shortcut.rectSelectSubtract",
    keys: (_, __, drag) => `Alt + ${drag}`,
  },
  {
    labelKey: "resource.shortcut.moreActions",
    keys: (_, __, ___, rightClick) => rightClick,
  },
];

interface Props {
  className?: string;
}

const ShortcutsButton = ({ className }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const modifierKey = useMemo(() => {
    const isMac =
      typeof navigator !== "undefined" && navigator.platform.toUpperCase().indexOf("MAC") >= 0;

    return isMac ? "⌘" : "Ctrl";
  }, []);

  const openModal = () => {
    const click = t<string>("resource.shortcut.key.click");
    const drag = t<string>("resource.shortcut.key.drag");
    const rightClick = t<string>("resource.shortcut.key.rightClick");

    createPortal(Modal, {
      defaultVisible: true,
      size: "sm",
      title: t<string>("resource.shortcut.title"),
      footer: { actions: ["cancel"] },
      children: (
        <div className="flex flex-col gap-3">
          {shortcuts.map((s, idx) => (
            <div key={idx} className="flex items-center justify-between">
              <span>{t<string>(s.labelKey)}</span>
              <Kbd>{s.keys(modifierKey, click, drag, rightClick)}</Kbd>
            </div>
          ))}
        </div>
      ),
    });
  };

  return (
    <Tooltip content={t<string>("resource.shortcut.title")}>
      <Button isIconOnly className={className} size={"sm"} variant={"light"} onPress={openModal}>
        <RiKeyboardLine className={"text-lg"} />
      </Button>
    </Tooltip>
  );
};

ShortcutsButton.displayName = "ShortcutsButton";

export default ShortcutsButton;
