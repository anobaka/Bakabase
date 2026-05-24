"use client";

import type { MarkConfig } from "../types";
import type { PreviewResultsByPath, PathMarkPreviewResult } from "../hooks/usePreview";
import type { PathMarkApplyScope } from "@/sdk/constants";

import { useState, useEffect } from "react";
import { EditOutlined } from "@ant-design/icons";

import MatchModeSelector from "./MatchModeSelector";
import PreviewResults from "./PreviewResults";

import { Button, Chip, Input, NumberInput, RadioGroup, Radio } from "@/components/bakaui";
import { PropertyValueType, PathMarkType } from "@/sdk/constants";
import BApi from "@/sdk/BApi";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import MediaLibrarySelectorV2 from "@/components/MediaLibrarySelectorV2";

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

interface MediaLibrary {
  id: number;
  name: string;
}

const MediaLibraryMarkConfig = ({
  config,
  updateConfig,
  t,
  priority,
  onPriorityChange,
  preview,
}: Props) => {
  const { createPortal } = useBakabaseContext();
  const [selectedLibrary, setSelectedLibrary] = useState<MediaLibrary | null>(null);

  // Load selected library info on mount if we have mediaLibraryId
  useEffect(() => {
    const loadLibrary = async () => {
      if (config.mediaLibraryId) {
        try {
          const response = await BApi.mediaLibraryV2.getAllMediaLibraryV2();
          const libraries = response?.data || [];
          const lib = libraries.find((l) => l.id === config.mediaLibraryId);

          if (lib) {
            setSelectedLibrary({ id: lib.id!, name: lib.name || `Library ${lib.id}` });
          }
        } catch (e) {
          console.error("Failed to load media library", e);
        }
      }
    };

    loadLibrary();
  }, []);

  const handleSelectLibrary = () => {
    createPortal(MediaLibrarySelectorV2, {
      onSelect: async (id: number) => {
        // Load the library name
        const response = await BApi.mediaLibraryV2.getAllMediaLibraryV2();
        const lib = response?.data?.find((l) => l.id === id);

        if (lib) {
          setSelectedLibrary({ id: lib.id!, name: lib.name || `Library ${lib.id}` });
          updateConfig({ mediaLibraryId: lib.id });
        }
      },
    });
  };

  const valueType = config.mediaLibraryValueType ?? PropertyValueType.Fixed;

  return (
    <>
      {/* Explanatory text */}
      <div className="bg-primary-50 text-primary-700 rounded p-2 text-xs">
        {t("pathMark.mediaLibrary.explanation")}
      </div>

      {/* Important note */}
      <div className="bg-warning-50 text-warning-700 rounded p-2 text-xs">
        {t("pathMark.mediaLibrary.importantNote")}
      </div>

      <MatchModeSelector
        config={config}
        markType={PathMarkType.MediaLibrary}
        t={t}
        updateConfig={updateConfig}
      />

      {/* Preview Results - placed after apply scope (part of MatchModeSelector) */}
      <PreviewResults
        applyScope={preview.applyScope}
        error={preview.error}
        isMultiplePaths={preview.isMultiplePaths}
        loading={preview.loading}
        markType={PathMarkType.MediaLibrary}
        results={preview.results}
        resultsByPath={preview.resultsByPath}
        t={t}
      />

      <div className="border-t border-default-200 pt-2">
        <span className="text-sm font-medium text-default-600">
          {t("pathMarkConfig.label.mediaLibrarySettings")}
        </span>
        <div className="text-xs text-default-400 mt-1">
          {t("pathMark.mediaLibrary.configDescription")}
        </div>
      </div>

      {/* Value Type - Radio Group */}
      <div className="flex flex-col gap-1">
        <span className="text-sm font-medium">{t("pathMarkConfig.label.valueType")}</span>
        <RadioGroup
          orientation="horizontal"
          size="sm"
          value={String(valueType)}
          onValueChange={(value) => updateConfig({ mediaLibraryValueType: Number(value) })}
        >
          <Radio value={String(PropertyValueType.Fixed)}>{t("pathMarkConfig.label.fixed")}</Radio>
          <Radio value={String(PropertyValueType.Dynamic)}>
            {t("pathMarkConfig.label.dynamic")}
          </Radio>
        </RadioGroup>
      </div>

      {valueType === PropertyValueType.Fixed ? (
        <>
          {/* Fixed Mode: Media Library Selector using MediaLibrarySelectorV2 */}
          <div className="flex flex-col gap-1">
            <span className="text-sm">{t("pathMarkConfig.label.targetMediaLibrary")}</span>
            <div className="flex items-center gap-2">
              {selectedLibrary ? (
                <Chip
                  color="secondary"
                  variant="flat"
                  onClose={() => {
                    setSelectedLibrary(null);
                    updateConfig({ mediaLibraryId: undefined });
                  }}
                >
                  {selectedLibrary.name}
                </Chip>
              ) : (
                <span className="text-sm text-default-400">
                  {t("pathMarkConfig.empty.noMediaLibrarySelected")}
                </span>
              )}
              <Button
                size="sm"
                startContent={<EditOutlined />}
                variant="flat"
                onPress={handleSelectLibrary}
              >
                {selectedLibrary
                  ? t("common.action.change")
                  : t("pathMarkConfig.action.selectMediaLibrary")}
              </Button>
            </div>
          </div>

          {/* Description for Fixed mode */}
          <div className="text-xs text-default-400 mt-2">
            {t("pathMarkConfig.tip.fixedMediaLibraryDescription")}
          </div>
        </>
      ) : (
        <>
          {/* Dynamic Mode: Layer or Regex to extract media library name */}
          <div className="text-xs text-default-400">
            {t("pathMarkConfig.tip.dynamicMediaLibraryDescription")}
          </div>

          <NumberInput
            description={t("pathMarkConfig.tip.layerToMediaLibraryDescription")}
            label={t("pathMarkConfig.label.layerToMediaLibrary")}
            size="sm"
            value={config.layerToMediaLibrary ?? 0}
            onValueChange={(v) => updateConfig({ layerToMediaLibrary: v })}
          />

          <Input
            description={t("pathMarkConfig.tip.regexToMediaLibraryDescription")}
            label={t("pathMarkConfig.label.regexToMediaLibrary")}
            placeholder={t("pathMarkConfig.tip.regexExample")}
            size="sm"
            value={config.regexToMediaLibrary ?? ""}
            onValueChange={(v) => updateConfig({ regexToMediaLibrary: v })}
          />
        </>
      )}

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

MediaLibraryMarkConfig.displayName = "MediaLibraryMarkConfig";

export default MediaLibraryMarkConfig;
