"use client";

import type { MarkConfig } from "../types";

import { Select } from "@/components/bakaui";
import { PathFilterFsType, pathFilterFsTypes } from "@/sdk/constants";
import ExtensionGroupSelect from "@/components/ExtensionGroupSelect";
import ExtensionsInput from "@/components/ExtensionsInput";

type Props = {
  config: MarkConfig;
  updateConfig: (updates: Partial<MarkConfig>) => void;
  t: (key: string) => string;
};

const ResourceMarkConfig = ({ config, updateConfig, t }: Props) => (
  <div className="flex flex-col gap-4">
    <div className="flex gap-2">
      <Select
        className="flex-1"
        dataSource={[
          { label: t("common.label.all"), value: "" },
          ...pathFilterFsTypes.map((t) => ({ label: t.label, value: String(t.value) })),
        ]}
        label={t("pathMarkConfig.label.fileTypeFilter")}
        selectedKeys={config.fsTypeFilter ? [String(config.fsTypeFilter)] : [""]}
        size="sm"
        onSelectionChange={(keys) => {
          const key = Array.from(keys)[0] as string;

          updateConfig({
            fsTypeFilter: key ? (parseInt(key, 10) as PathFilterFsType) : undefined,
          });
        }}
      />
      {(config.fsTypeFilter === undefined || config.fsTypeFilter === PathFilterFsType.File) && (
        <ExtensionGroupSelect
          className="flex-1"
          size="sm"
          value={config.extensionGroupIds}
          onSelectionChange={(ids) => updateConfig({ extensionGroupIds: ids })}
        />
      )}
    </div>

    {(config.fsTypeFilter === undefined || config.fsTypeFilter === PathFilterFsType.File) && (
      <ExtensionsInput
        defaultValue={config.extensions}
        label={t("pathMarkConfig.label.limitExtensions")}
        minRows={1}
        size="sm"
        onValueChange={(v) => updateConfig({ extensions: v })}
      />
    )}
  </div>
);

export default ResourceMarkConfig;
