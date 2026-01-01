"use client";

import React, { useMemo } from "react";
import { useTranslation } from "react-i18next";
import { RiKeyboardLine } from "react-icons/ri";

import type { FileExplorerEntryProps } from "../FileExplorerEntry";
import { Kbd, Button, Modal, Tooltip } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { FileSystemTreeEntryCapabilityMap } from "../models";
import { buildLogger } from "@/components/utils";

type Props = Pick<FileExplorerEntryProps, "capabilities"> & {
  className?: string;
};

const log = buildLogger('Shortcuts');

const Shortcuts = ({ capabilities, className }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  log('shortcuts for capabilities', capabilities);

  const shortcuts = useMemo(() => {
    const result: { label: string; shortcut: string }[] = [];

    // Add capability-based shortcuts
    if (capabilities && capabilities.length > 0) {
      capabilities.forEach((c) => {
        const def = FileSystemTreeEntryCapabilityMap[c];
        const shortcut = def?.shortcut;
        if (shortcut) {
          result.push({
            label: t<string>(def.nameI18NKey),
            shortcut: t<string>(def.shortcut!.nameI18nKey!),
          });
        }
      });
    }

    // Add navigation shortcuts (always available)
    result.push(
      { label: t<string>("Select previous"), shortcut: "↑" },
      { label: t<string>("Select next"), shortcut: "↓" },
      { label: t<string>("Enter directory"), shortcut: "Enter" },
      { label: t<string>("Select all"), shortcut: "Ctrl+A" },
      { label: t<string>("Copy"), shortcut: "Ctrl+C" },
      { label: t<string>("Cut"), shortcut: "Ctrl+X" },
      { label: t<string>("Paste (move files)"), shortcut: "Ctrl+V" },
    );

    return result;
  }, [capabilities, t]);

  const openModal = () => {
    createPortal(Modal, {
      defaultVisible: true,
      size: "lg",
      title: t<string>("Shortcuts"),
      footer: { actions: ["cancel"] },
      children: (
        <div className="grid grid-cols-2 gap-x-8 gap-y-2">
          {shortcuts.map((s, idx) => (
            <div key={idx} className="flex items-center justify-between">
              <div className="text-sm">{s.label}</div>
              <Kbd>{s.shortcut}</Kbd>
            </div>
          ))}
        </div>
      ),
    });
  };

  return (
    <Tooltip content={t<string>("Shortcuts")}>
      <Button isIconOnly className={className} variant={"light"} onPress={openModal}>
        <RiKeyboardLine className={"text-lg"} />
      </Button>
    </Tooltip>
  );
};

Shortcuts.displayName = "Shortcuts";

export default Shortcuts;


