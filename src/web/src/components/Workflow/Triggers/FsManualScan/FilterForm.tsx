import type { FsManualScanFilter } from "./types";

import React from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineDelete, AiOutlineFolderAdd } from "react-icons/ai";

import { Button, Chip, Input, NumberInput, Select } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { FileSystemSelectorModal } from "@/components/FileSystemSelector";
import { FsScanTarget, fsScanTargets } from "@/sdk/constants";

interface Props {
  value: FsManualScanFilter;
  onChange: (v: FsManualScanFilter) => void;
}

const FilterForm: React.FC<Props> = ({ value, onChange }) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const addRoots = () => {
    createPortal(FileSystemSelectorModal, {
      multiple: true,
      onMultipleSelected: (entries) => {
        const incoming = entries.map((e) => e.path).filter(Boolean) as string[];
        const merged = Array.from(new Set([...value.roots, ...incoming.map((p) => p.trim())]));

        onChange({ ...value, roots: merged.filter(Boolean) });
      },
    });
  };

  return (
    <div className="flex flex-col gap-2">
      <div className="flex items-center gap-2">
        <span className="text-sm">{t<string>("workflow.trigger.fsManualScan.roots.label")}</span>
        <Button size="sm" startContent={<AiOutlineFolderAdd />} variant="flat" onPress={addRoots}>
          {t<string>("workflow.trigger.fsManualScan.roots.add")}
        </Button>
      </div>
      {value.roots.length === 0 ? (
        <div className="text-xs text-warning">
          {t<string>("workflow.trigger.fsManualScan.roots.empty")}
        </div>
      ) : (
        <div className="flex flex-col gap-1">
          {value.roots.map((root) => (
            <div key={root} className="flex items-center gap-1 text-sm">
              <Button
                isIconOnly
                color="danger"
                size="sm"
                variant="light"
                onPress={() => onChange({ ...value, roots: value.roots.filter((r) => r !== root) })}
              >
                <AiOutlineDelete />
              </Button>
              <span className="break-all">{root}</span>
            </div>
          ))}
        </div>
      )}

      <div className="grid grid-cols-2 gap-2">
        <Select
          dataSource={fsScanTargets.map(({ value: v }) => ({
            value: String(v),
            label: t<string>(`workflow.trigger.fsManualScan.target.${FsScanTarget[v]}`),
          }))}
          label={t<string>("workflow.trigger.fsManualScan.target.label")}
          selectedKeys={[String(value.target)]}
          onSelectionChange={(keys) => {
            const k = Array.from(keys)[0];

            if (k != undefined) onChange({ ...value, target: Number(k) as FsScanTarget });
          }}
        />
        <NumberInput
          description={t<string>("workflow.trigger.fsManualScan.depth.description")}
          label={t<string>("workflow.trigger.fsManualScan.depth.label")}
          minValue={1}
          value={value.depth}
          onValueChange={(v) => onChange({ ...value, depth: Math.max(1, Math.trunc(v || 1)) })}
        />
      </div>

      <Input
        description={t<string>("workflow.trigger.fsManualScan.extensions.description")}
        label={t<string>("workflow.trigger.fsManualScan.extensions.label")}
        value={value.extensionFilter.join(", ")}
        onValueChange={(v) =>
          onChange({
            ...value,
            extensionFilter: v
              .split(/[,，\s]+/)
              .map((e) => e.trim().replace(/^\./, "").toLowerCase())
              .filter(Boolean),
          })
        }
      />
      {value.target === FsScanTarget.Directories && value.extensionFilter.length > 0 && (
        <Chip color="warning" size="sm" variant="flat">
          {t<string>("workflow.trigger.fsManualScan.extensions.ignoredForDirectories")}
        </Chip>
      )}
    </div>
  );
};

export default FilterForm;
