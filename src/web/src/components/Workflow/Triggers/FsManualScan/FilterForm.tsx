import type { FsManualScanFilter } from "./types";

import React from "react";
import { useTranslation } from "react-i18next";

import { FsRootsPicker, parseExtensions } from "../FsShared";

import { Chip, Input, NumberInput, Select } from "@/components/bakaui";
import { FsScanTarget, fsScanTargets } from "@/sdk/constants";

interface Props {
  value: FsManualScanFilter;
  onChange: (v: FsManualScanFilter) => void;
}

const FilterForm: React.FC<Props> = ({ value, onChange }) => {
  const { t } = useTranslation();

  return (
    <div className="flex flex-col gap-2">
      <FsRootsPicker roots={value.roots} onChange={(roots) => onChange({ ...value, roots })} />

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
        onValueChange={(v) => onChange({ ...value, extensionFilter: parseExtensions(v) })}
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
