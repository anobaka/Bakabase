"use client";

import type { MarkConfig } from "../types";

import { useState, useEffect } from "react";
import { usePreview } from "../hooks/usePreview";
import MatchModeSelector from "./MatchModeSelector";
import PreviewResults from "./PreviewResults";
import { Chip, Input, Select, Spinner, RadioGroup, Radio } from "@/components/bakaui";
import { PathMarkType, PropertyValueType } from "@/sdk/constants";
import BApi from "@/sdk/BApi";

type Props = {
  config: MarkConfig;
  updateConfig: (updates: Partial<MarkConfig>) => void;
  rootPath?: string;
  rootPaths?: string[];
  t: (key: string) => string;
  priority: number;
  onPriorityChange: (priority: number) => void;
};

interface MediaLibrary {
  id: number;
  name: string;
}

const MediaLibraryMarkConfig = ({ config, updateConfig, rootPath, rootPaths, t, priority, onPriorityChange }: Props) => {
  const preview = usePreview(rootPath, PathMarkType.MediaLibrary, config, 500, rootPaths);
  const [mediaLibraries, setMediaLibraries] = useState<MediaLibrary[]>([]);
  const [loading, setLoading] = useState(true);
  const [selectedLibrary, setSelectedLibrary] = useState<MediaLibrary | null>(null);

  // Load all media libraries on mount
  useEffect(() => {
    const loadMediaLibraries = async () => {
      try {
        const response = await BApi.mediaLibraryV2.getAllMediaLibrariesV2();
        const libraries = (response?.data || []).map((lib) => ({
          id: lib.id!,
          name: lib.name || `Library ${lib.id}`,
        }));
        setMediaLibraries(libraries);

        // If we have a mediaLibraryId in config, find and set the selected library
        if (config.mediaLibraryId) {
          const lib = libraries.find((l) => l.id === config.mediaLibraryId);
          if (lib) {
            setSelectedLibrary(lib);
          }
        }
      } catch (e) {
        console.error("Failed to load media libraries", e);
      } finally {
        setLoading(false);
      }
    };
    loadMediaLibraries();
  }, []);

  const handleSelectLibrary = (libraryId: number) => {
    const lib = mediaLibraries.find((l) => l.id === libraryId);
    if (lib) {
      setSelectedLibrary(lib);
      updateConfig({ mediaLibraryId: lib.id });
    }
  };

  const valueType = config.mediaLibraryValueType ?? PropertyValueType.Fixed;

  return (
    <>
      <MatchModeSelector
        config={config}
        updateConfig={updateConfig}
        t={t}
        priority={priority}
        onPriorityChange={onPriorityChange}
      />

      <PreviewResults
        loading={preview.loading}
        results={preview.results}
        resultsByPath={preview.resultsByPath}
        isMultiplePaths={preview.isMultiplePaths}
        error={preview.error}
        t={t}
      />

      <div className="border-t border-default-200 pt-2">
        <span className="text-sm font-medium text-default-600">{t("Media Library Settings")}</span>
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
          {/* Fixed Mode: Media Library Selector */}
          <div className="flex flex-col gap-1">
            <span className="text-sm">{t("Target Media Library")}</span>
            {loading ? (
              <div className="flex items-center gap-2">
                <Spinner size="sm" />
                <span className="text-sm text-default-400">{t("Loading media libraries...")}</span>
              </div>
            ) : mediaLibraries.length === 0 ? (
              <span className="text-sm text-warning">{t("No media libraries available. Please create one first.")}</span>
            ) : (
              <Select
                label={t("Select Media Library")}
                placeholder={t("Choose a media library")}
                selectedKeys={selectedLibrary ? [String(selectedLibrary.id)] : []}
                onSelectionChange={(keys) => {
                  const selectedKey = Array.from(keys)[0];
                  if (selectedKey) {
                    handleSelectLibrary(Number(selectedKey));
                  }
                }}
                size="sm"
                dataSource={mediaLibraries.map((lib) => ({
                  value: String(lib.id),
                  label: lib.name,
                }))}
              />
            )}
          </div>

          {/* Selected library display */}
          {selectedLibrary && (
            <div className="flex items-center gap-2 mt-2">
              <span className="text-sm text-default-500">{t("Selected")}:</span>
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
            </div>
          )}

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

          <Input
            label={t("Layer to Media Library")}
            description={t("Which layer's directory name to use as media library name. 0 = matched item, 1 = parent, etc.")}
            type="number"
            size="sm"
            value={String(config.layerToMediaLibrary ?? 0)}
            onValueChange={(v) => updateConfig({ layerToMediaLibrary: parseInt(v) || 0 })}
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
    </>
  );
};

MediaLibraryMarkConfig.displayName = "MediaLibraryMarkConfig";

export default MediaLibraryMarkConfig;
