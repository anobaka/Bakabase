"use client";

import type { MarkConfig } from "../types";
import type { PreviewResultsByPath, PathMarkPreviewResult } from "../hooks/usePreview";
import MatchModeSelector from "./MatchModeSelector";
import PreviewResults from "./PreviewResults";
import { Select, NumberInput, Switch } from "@/components/bakaui";
import { PathFilterFsType, pathFilterFsTypes, PathMarkType, PathMarkApplyScope } from "@/sdk/constants";
import ExtensionGroupSelect from "@/components/ExtensionGroupSelect";
import ExtensionsInput from "@/components/ExtensionsInput";

type Props = {
  config: MarkConfig;
  updateConfig: (updates: Partial<MarkConfig>) => void;
  t: (key: string) => string;
  priority: number;
  onPriorityChange: (priority: number) => void;
  preview: {
    loading: boolean;
    results: PathMarkPreviewResult[];
    resultsByPath?: PreviewResultsByPath[];
    isMultiplePaths?: boolean;
    error: string | null;
    applyScope?: PathMarkApplyScope;
  };
};

const ResourceMarkConfig = ({ config, updateConfig, t, priority, onPriorityChange, preview }: Props) => {
  return (
    <>
      {/* Explanatory text */}
      <div className="bg-primary-50 text-primary-700 rounded p-2 text-xs">
        {t("PathMark.Resource.Explanation")}
      </div>

      <MatchModeSelector
        config={config}
        updateConfig={updateConfig}
        t={t}
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

      {/* Preview Results - placed after resource filters */}
      <PreviewResults
        loading={preview.loading}
        results={preview.results}
        resultsByPath={preview.resultsByPath}
        isMultiplePaths={preview.isMultiplePaths}
        error={preview.error}
        markType={PathMarkType.Resource}
        t={t}
        applyScope={preview.applyScope}
      />

      {/* Resource Boundary Option */}
      <div className="border-t border-default-200 pt-2">
        <Switch
          size="sm"
          isSelected={config.isResourceBoundary ?? false}
          onValueChange={(v) => updateConfig({ isResourceBoundary: v })}
        >
          <div className="flex flex-col">
            <span className="text-sm">{t("PathMark.Resource.IsResourceBoundary")}</span>
            <span className="text-xs text-default-400">{t("PathMark.Resource.IsResourceBoundary.Description")}</span>
          </div>
        </Switch>
      </div>

      {/* Priority at the bottom */}
      <div className="border-t border-default-200 pt-2">
        <NumberInput
          label={t("Priority")}
          description={t("PathMark.Priority.Description")}
          size="sm"
          value={priority}
          onValueChange={(v) => onPriorityChange(v ?? 10)}
        />
      </div>
    </>
  );
};

ResourceMarkConfig.displayName = "ResourceMarkConfig";

export default ResourceMarkConfig;
