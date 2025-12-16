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
  rootPaths?: string[];
  t: (key: string) => string;
  priority: number;
  onPriorityChange: (priority: number) => void;
};

const ResourceMarkConfig = ({ config, updateConfig, rootPath, rootPaths, t, priority, onPriorityChange }: Props) => {
  const preview = usePreview(rootPath, PathMarkType.Resource, config, 500, rootPaths);

  return (
    <>
      <MatchModeSelector
        config={config}
        updateConfig={updateConfig}
        t={t}
        priority={priority}
        onPriorityChange={onPriorityChange}
      />

      <div className="border-t border-default-200 pt-2">
        <span className="text-sm font-medium text-default-600">{t("Resource Filters (Optional)")}</span>
      </div>

      <div className="flex gap-2">
        <Select
          label={t("File Type Filter")}
          size="sm"
          className="flex-1"
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
          <ExtensionGroupSelect
            value={config.extensionGroupIds}
            onSelectionChange={(ids) => updateConfig({ extensionGroupIds: ids })}
            size="sm"
            className="flex-1"
          />
        )}
      </div>

      {(config.fsTypeFilter === undefined || config.fsTypeFilter === PathFilterFsType.File) && (
        <ExtensionsInput
          defaultValue={config.extensions}
          label={t("Limit file extensions")}
          onValueChange={(v) => updateConfig({ extensions: v })}
          minRows={1}
          size="sm"
        />
      )}

      <PreviewResults
        loading={preview.loading}
        results={preview.results}
        resultsByPath={preview.resultsByPath}
        isMultiplePaths={preview.isMultiplePaths}
        error={preview.error}
        markType={PathMarkType.Resource}
        t={t}
      />
    </>
  );
};

ResourceMarkConfig.displayName = "ResourceMarkConfig";

export default ResourceMarkConfig;
