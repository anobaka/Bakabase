"use client";

import type { MarkConfig } from "../types";

import { useState, useEffect } from "react";
import { EditOutlined } from "@ant-design/icons";

import DynamicValueConfig from "./DynamicValueConfig";

import { Button, Chip, RadioGroup, Radio } from "@/components/bakaui";
import { PropertyValueType, PathMarkType } from "@/sdk/constants";
import BApi from "@/sdk/BApi";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import MediaLibrarySelectorV2 from "@/components/MediaLibrarySelectorV2";

type Props = {
  config: MarkConfig;
  updateConfig: (updates: Partial<MarkConfig>) => void;
  t: (key: string) => string;
};

interface MediaLibrary {
  id: number;
  name: string;
}

const MediaLibraryMarkConfig = ({ config, updateConfig, t }: Props) => {
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
    <div className="flex flex-col gap-4">
      <RadioGroup
        label={t("pathMarkConfig.label.valueType")}
        orientation="horizontal"
        size="sm"
        value={String(valueType)}
        onValueChange={(value) => updateConfig({ mediaLibraryValueType: Number(value) })}
      >
        <Radio value={String(PropertyValueType.Fixed)}>{t("pathMarkConfig.label.fixed")}</Radio>
        <Radio value={String(PropertyValueType.Dynamic)}>{t("pathMarkConfig.label.dynamic")}</Radio>
      </RadioGroup>
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

          <p className="text-xs leading-relaxed text-default-500">
            {t("pathMarkConfig.tip.fixedMediaLibraryDescription")}
          </p>
        </>
      ) : (
        <DynamicValueConfig
          config={config}
          markType={PathMarkType.MediaLibrary}
          t={t}
          updateConfig={updateConfig}
        />
      )}
    </div>
  );
};

export default MediaLibraryMarkConfig;
