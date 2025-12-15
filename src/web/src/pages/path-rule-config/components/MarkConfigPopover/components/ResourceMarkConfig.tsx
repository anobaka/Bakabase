"use client";

import type { MarkConfig } from "../types";
import { usePreview } from "../hooks/usePreview";
import MatchModeSelector from "./MatchModeSelector";
import PreviewResults from "./PreviewResults";
import { Select } from "@/components/bakaui";
import { PathMarkType, PathFilterFsType, pathFilterFsTypes } from "@/sdk/constants";
import ExtensionGroupSelect from "@/components/ExtensionGroupSelect";
import ExtensionsInput from "@/components/ExtensionsInput";

type Props = {
  config: MarkConfig;
  updateConfig: (updates: Partial<MarkConfig>) => void;
  rootPath?: string;
  t: (key: string) => string;
};

const ResourceMarkConfig = ({ config, updateConfig, rootPath, t }: Props) => {
  const preview = usePreview(rootPath, PathMarkType.Resource, config);

  return (
    <>
      <MatchModeSelector config={config} updateConfig={updateConfig} t={t} />

      <div className="border-t border-default-200 pt-3">
        <span className="text-sm font-medium text-default-600">{t("Resource Filters (Optional)")}</span>
      </div>

      <Select
        label={t("File Type Filter")}
        description={t("Filter by file system type")}
        size="sm"
        dataSource={[
          { label: t("All"), value: "" },
          ...pathFilterFsTypes.map(t => ({ label: t.label, value: String(t.value) })),
        ]}
        selectedKeys={config.fsTypeFilter ? [String(config.fsTypeFilter)] : [""]}
        onSelectionChange={(keys) => {
          const key = Array.from(keys)[0] as string;
          updateConfig({ fsTypeFilter: key ? parseInt(key, 10) as PathFilterFsType : undefined });
        }}
      />

      {(config.fsTypeFilter === undefined || config.fsTypeFilter === PathFilterFsType.File) && (
        <>
          <ExtensionGroupSelect
            value={config.extensionGroupIds}
            onSelectionChange={(ids) => updateConfig({ extensionGroupIds: ids })}
          />
          <ExtensionsInput
            defaultValue={config.extensions}
            label={t("Limit file extensions")}
            onValueChange={(v) => updateConfig({ extensions: v })}
          />
        </>
      )}

      <PreviewResults
        loading={preview.loading}
        results={preview.results}
        error={preview.error}
        t={t}
      />
    </>
  );
};

ResourceMarkConfig.displayName = "ResourceMarkConfig";

export default ResourceMarkConfig;
