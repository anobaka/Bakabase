"use client";

import React from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineDelete, AiOutlineFolderAdd } from "react-icons/ai";

import { Button } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { FileSystemSelectorModal } from "@/components/FileSystemSelector";

/**
 * The roots picker every fs trigger form shares — one implementation so the scan, scheduled
 * and watch forms cannot drift apart.
 */
export const FsRootsPicker: React.FC<{
  roots: string[];
  onChange: (roots: string[]) => void;
}> = ({ roots, onChange }) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const addRoots = () => {
    createPortal(FileSystemSelectorModal, {
      multiple: true,
      onMultipleSelected: (entries) => {
        const incoming = entries.map((e) => e.path).filter(Boolean) as string[];
        const merged = Array.from(new Set([...roots, ...incoming.map((p) => p.trim())]));

        onChange(merged.filter(Boolean));
      },
    });
  };

  return (
    <>
      <div className="flex items-center gap-2">
        <span className="text-sm">{t<string>("workflow.trigger.fsManualScan.roots.label")}</span>
        <Button size="sm" startContent={<AiOutlineFolderAdd />} variant="flat" onPress={addRoots}>
          {t<string>("workflow.trigger.fsManualScan.roots.add")}
        </Button>
      </div>
      {roots.length === 0 ? (
        <div className="text-xs text-warning">
          {t<string>("workflow.trigger.fsManualScan.roots.empty")}
        </div>
      ) : (
        <div className="flex flex-col gap-1">
          {roots.map((root) => (
            <div key={root} className="flex items-center gap-1 text-sm">
              <Button
                isIconOnly
                color="danger"
                size="sm"
                variant="light"
                onPress={() => onChange(roots.filter((r) => r !== root))}
              >
                <AiOutlineDelete />
              </Button>
              <span className="break-all">{root}</span>
            </div>
          ))}
        </div>
      )}
    </>
  );
};

/** Comma/space separated extension list → normalized array. Shared by the fs filter forms. */
export function parseExtensions(raw: string): string[] {
  return raw
    .split(/[,，\s]+/)
    .map((e) => e.trim().replace(/^\./, "").toLowerCase())
    .filter(Boolean);
}
