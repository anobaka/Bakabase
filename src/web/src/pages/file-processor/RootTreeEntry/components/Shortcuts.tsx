"use client";

import React, { useMemo } from "react";
import { useTranslation } from "react-i18next";
import { RiKeyboardLine } from "react-icons/ri";

import type { TreeEntryProps } from "@/pages/file-processor/TreeEntry";
import { Kbd, Button, Modal, Tooltip } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { FileSystemTreeEntryCapabilityMap } from "../models";
import { buildLogger } from "@/components/utils";

type Props = Pick<TreeEntryProps, "capabilities"> & {
  className?: string;
};

const log = buildLogger('Shortcuts');

const Shortcuts = ({ capabilities, className }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  log('shortcuts for capabilities', capabilities);

  const shortcuts = useMemo(() => {
    if (!capabilities || capabilities.length === 0) return [] as { label: string; shortcut: string }[];

    return capabilities
      .map((c) => {
        const def = FileSystemTreeEntryCapabilityMap[c];
        const shortcut = def?.shortcut;
        if (!shortcut) return undefined;
        return {
          label: t<string>(def.nameI18NKey),
          shortcut: t<string>(def.shortcut!.nameI18nKey!),
        };
      })
      .filter((x): x is { label: string; shortcut: string } => !!x);
  }, [capabilities, t]);

  if (!shortcuts.length) return null;

  const openModal = () => {
    createPortal(Modal, {
      defaultVisible: true,
      size: "sm",
      title: t<string>("Shortcuts"),
      footer: { actions: ["cancel"] },
      children: (
        <div className="flex flex-col gap-2">
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


