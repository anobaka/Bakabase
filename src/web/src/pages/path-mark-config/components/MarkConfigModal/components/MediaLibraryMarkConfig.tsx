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
  const [libraries, setLibraries] = useState<MediaLibrary[]>([]);
  const selectedIds =
    config.mediaLibraryIds ?? (config.mediaLibraryId ? [config.mediaLibraryId] : []);
  const updateLibraries = (ids: number[]) =>
    updateConfig({
      mediaLibraryIds: ids,
      mediaLibraryId: ids[0],
    });

  useEffect(() => {
    let cancelled = false;
    const loadLibraries = async () => {
      try {
        const response = await BApi.mediaLibraryV2.getAllMediaLibraryV2();

        if (!response.code && !cancelled) {
          setLibraries(
            (response.data ?? []).map((library) => ({ id: library.id, name: library.name })),
          );
        }
      } catch (error) {
        console.error("Failed to load media libraries", error);
      }
    };

    void loadLibraries();

    return () => {
      cancelled = true;
    };
  }, []);

  const handleSelectLibrary = () => {
    createPortal(MediaLibrarySelectorV2, {
      onSelect: async (id: number) => {
        const response = await BApi.mediaLibraryV2.getAllMediaLibraryV2();

        if (response.code) throw new Error(response.message);
        setLibraries(
          (response.data ?? []).map((library) => ({ id: library.id, name: library.name })),
        );
        updateLibraries([...new Set([...selectedIds, id])]);
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
          <div className="flex flex-col gap-1">
            <span className="text-sm">{t("pathMarkConfig.label.targetMediaLibrary")}</span>
            <div className="flex flex-wrap items-center gap-2">
              {selectedIds.length > 0 ? (
                selectedIds.map((id) => (
                  <Chip
                    key={id}
                    color="secondary"
                    variant="flat"
                    onClose={() =>
                      updateLibraries(selectedIds.filter((selectedId) => selectedId !== id))
                    }
                  >
                    {libraries.find((library) => library.id === id)?.name ||
                      `${t("mediaLibrary.title")} #${id}`}
                  </Chip>
                ))
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
                {t("pathMarkConfig.action.addMediaLibrary")}
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
