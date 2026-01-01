"use client";

import type { MarkConfig } from "../types";
import type { PreviewResultsByPath, PathMarkPreviewResult } from "../hooks/usePreview";

import { useState, useEffect } from "react";
import MatchModeSelector from "./MatchModeSelector";
import PreviewResults from "./PreviewResults";
import { Button, Chip, Input, NumberInput, RadioGroup, Radio } from "@/components/bakaui";
import { PropertyValueType, PathMarkType, PathMarkApplyScope } from "@/sdk/constants";
import BApi from "@/sdk/BApi";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import MediaLibrarySelectorV2 from "@/components/MediaLibrarySelectorV2";
import { EditOutlined } from "@ant-design/icons";

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

const MediaLibraryMarkConfig = ({ config, updateConfig, t, priority, onPriorityChange, preview }: Props) => {
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
        {t("PathMark.MediaLibrary.Explanation")}
      </div>

      <MatchModeSelector
        config={config}
        updateConfig={updateConfig}
        t={t}
      />

      {/* Preview Results - placed after apply scope (part of MatchModeSelector) */}
      <PreviewResults
        loading={preview.loading}
        results={preview.results}
        resultsByPath={preview.resultsByPath}
        isMultiplePaths={preview.isMultiplePaths}
        error={preview.error}
        markType={PathMarkType.MediaLibrary}
        t={t}
        applyScope={preview.applyScope}
      />

      <div className="border-t border-default-200 pt-2">
        <span className="text-sm font-medium text-default-600">{t("Media Library Settings")}</span>
        <div className="text-xs text-default-400 mt-1">{t("PathMark.MediaLibrary.SettingsDescription")}</div>
      </div>

      {/* Value Type - Radio Group */}
      <div className="flex flex-col gap-1">
        <span className="text-sm font-medium">{t("Value Type")}</span>
        <RadioGroup
          value={String(valueType)}
          onValueChange={(value) => updateConfig({ mediaLibraryValueType: Number(value) })}
          size="sm"
          orientation="horizontal"
        >
          <Radio value={String(PropertyValueType.Fixed)}>
            {t("Fixed")}
          </Radio>
          <Radio value={String(PropertyValueType.Dynamic)}>
            {t("Dynamic")}
          </Radio>
        </RadioGroup>
      </div>

      {valueType === PropertyValueType.Fixed ? (
        <>
          {/* Fixed Mode: Media Library Selector using MediaLibrarySelectorV2 */}
          <div className="flex flex-col gap-1">
            <span className="text-sm">{t("Target Media Library")}</span>
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
                <span className="text-sm text-default-400">{t("No media library selected")}</span>
              )}
              <Button
                size="sm"
                variant="flat"
                startContent={<EditOutlined />}
                onPress={handleSelectLibrary}
              >
                {selectedLibrary ? t("Change") : t("Select Media Library")}
              </Button>
            </div>
          </div>

          {/* Description for Fixed mode */}
          <div className="text-xs text-default-400 mt-2">
            {t("Resources matched by this mark will be associated with the selected media library.")}
          </div>
        </>
      ) : (
        <>
          {/* Dynamic Mode: Layer or Regex to extract media library name */}
          <div className="text-xs text-default-400">
            {t("Extract the media library name from the path. If the media library does not exist, it will be created automatically.")}
          </div>

          <NumberInput
            label={t("Layer to Media Library")}
            description={t("Which layer's directory name to use as media library name. 0 = matched item, 1 = parent, etc.")}
            size="sm"
            value={config.layerToMediaLibrary ?? 0}
            onValueChange={(v) => updateConfig({ layerToMediaLibrary: v })}
          />

          <Input
            label={t("Regex to Media Library (Optional)")}
            description={t("Optional regex to extract media library name. Uses first capture group or full match.")}
            placeholder={t("e.g., \\[(.+?)\\]")}
            size="sm"
            value={config.regexToMediaLibrary ?? ""}
            onValueChange={(v) => updateConfig({ regexToMediaLibrary: v })}
          />
        </>
      )}

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

MediaLibraryMarkConfig.displayName = "MediaLibraryMarkConfig";

export default MediaLibraryMarkConfig;
