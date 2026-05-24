"use client";

import type { MarkConfig } from "../types";
import type { PreviewResultsByPath, PathMarkPreviewResult } from "../hooks/usePreview";
import type { PathMarkApplyScope } from "@/sdk/constants";

import MatchModeSelector from "./MatchModeSelector";
import PreviewResults from "./PreviewResults";

import { Select, NumberInput, Switch } from "@/components/bakaui";
import { PathFilterFsType, pathFilterFsTypes, PathMarkType } from "@/sdk/constants";
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

const ResourceMarkConfig = ({
  config,
  updateConfig,
  t,
  priority,
  onPriorityChange,
  preview,
}: Props) => {
  return (
    <>
      {/* Explanatory text */}
      <div className="bg-primary-50 text-primary-700 rounded p-2 text-xs">
        {t("pathMark.resource.explanation")}
      </div>

      <MatchModeSelector
        config={config}
        markType={PathMarkType.Resource}
        t={t}
        updateConfig={updateConfig}
      />

      <div className="border-t border-default-200 pt-2">
        <span className="text-sm font-medium text-default-600">
          {t("pathMarkConfig.label.resourceFilters")}
        </span>
      </div>

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

      {/* Preview Results - placed after resource filters */}
      <PreviewResults
        applyScope={preview.applyScope}
        error={preview.error}
        isMultiplePaths={preview.isMultiplePaths}
        loading={preview.loading}
        markType={PathMarkType.Resource}
        results={preview.results}
        resultsByPath={preview.resultsByPath}
        t={t}
      />

      {/* Resource Boundary Option */}
      <div className="border-t border-default-200 pt-2">
        <Switch
          isSelected={config.isResourceBoundary ?? false}
          size="sm"
          onValueChange={(v) => updateConfig({ isResourceBoundary: v })}
        >
          <div className="flex flex-col">
            <span className="text-sm">{t("pathMark.resource.isResourceBoundary")}</span>
            <span className="text-xs text-default-400">
              {t("pathMark.resource.isResourceBoundary.description")}
            </span>
          </div>
        </Switch>
      </div>

      {/* Priority at the bottom */}
      <div className="border-t border-default-200 pt-2">
        <NumberInput
          description={t("pathMark.priority.description")}
          label={t("common.label.priority")}
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
