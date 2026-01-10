"use client";

import React, { useMemo } from "react";
import { useTranslation } from "react-i18next";
import { RiKeyboardLine } from "react-icons/ri";

import { Button, Kbd, Modal, Tooltip } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

interface Shortcut {
  labelKey: string;
  keys: string | ((modifierKey: string) => string);
}

const shortcuts: Shortcut[] = [
  {
    labelKey: "Select multiple resources",
    keys: (mod) => `${mod} + Click`,
  },
  {
    labelKey: "Select range of resources",
    keys: "Shift + Click",
  },
  {
    labelKey: "More actions",
    keys: "Right click",
  },
];

interface Props {
  className?: string;
}

const ShortcutsButton = ({ className }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const modifierKey = useMemo(() => {
    const isMac = typeof navigator !== "undefined" && navigator.platform.toUpperCase().indexOf("MAC") >= 0;
    return isMac ? "⌘" : "Ctrl";
  }, []);

  const openModal = () => {
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
              <Kbd>
                {typeof s.keys === "function"
                  ? s.keys(modifierKey)
                  : t<string>(s.keys)}
              </Kbd>
            </div>
          ))}
        </div>
      ),
    });
  };

  return (
    <Tooltip content={t<string>("resource.shortcut.title")}>
      <Button
        isIconOnly
        className={className}
        size={"sm"}
        variant={"light"}
        onPress={openModal}
      >
        <RiKeyboardLine className={"text-lg"} />
      </Button>
    </Tooltip>
  );
};

ShortcutsButton.displayName = "ShortcutsButton";

export default ShortcutsButton;
