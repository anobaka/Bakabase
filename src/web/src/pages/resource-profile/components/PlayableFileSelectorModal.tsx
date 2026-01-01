"use client";

import type { DestroyableProps } from "@/components/bakaui/types.ts";
import type { BakabaseAbstractionsModelsDomainResourceProfilePlayableFileOptions } from "@/sdk/Api";

import { useState } from "react";
import { useTranslation } from "react-i18next";
import { InfoCircleOutlined } from "@ant-design/icons";

import { Modal, Input, Chip, Divider } from "@/components/bakaui";
import ExtensionsInput from "@/components/ExtensionsInput";

type PlayableFileOptions = BakabaseAbstractionsModelsDomainResourceProfilePlayableFileOptions;

type Props = {
  options?: PlayableFileOptions;
  onSubmit?: (options: PlayableFileOptions) => any;
} & DestroyableProps;

// Common extension presets
const extensionPresets = {
  video: ["mp4", "mkv", "avi", "wmv", "mov", "flv", "webm", "m4v", "rmvb", "rm"],
  audio: ["mp3", "flac", "wav", "aac", "ogg", "wma", "m4a", "ape"],
  image: ["jpg", "jpeg", "png", "gif", "bmp", "webp", "tiff", "svg"],
  document: ["pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "txt"],
  archive: ["zip", "rar", "7z", "tar", "gz"],
};

const PlayableFileSelectorModal = ({ options: propOptions, onSubmit, onDestroyed }: Props) => {
  const { t } = useTranslation();

  const [options, setOptions] = useState<PlayableFileOptions>(propOptions ?? {});

  const addPresetExtensions = (preset: keyof typeof extensionPresets) => {
    const currentExts = new Set(options.extensions ?? []);
    extensionPresets[preset].forEach((ext) => currentExts.add(ext));
    setOptions({
      ...options,
      extensions: Array.from(currentExts),
    });
  };

  const clearExtensions = () => {
    setOptions({
      ...options,
      extensions: [],
    });
  };

  return (
    <Modal
      defaultVisible
      size="xl"
      title={t("Playable File Options")}
      onDestroyed={onDestroyed}
      onOk={() => onSubmit?.(options)}
    >
      <div className="flex flex-col gap-4">
        {/* Quick presets */}
        <div>
          <div className="text-sm font-medium mb-2">{t("Quick Add Presets")}</div>
          <div className="flex flex-wrap gap-2">
            <Chip
              size="sm"
              color="primary"
              variant="flat"
              className="cursor-pointer hover:opacity-80"
              onClick={() => addPresetExtensions("video")}
            >
              {t("Video")}
            </Chip>
            <Chip
              size="sm"
              color="secondary"
              variant="flat"
              className="cursor-pointer hover:opacity-80"
              onClick={() => addPresetExtensions("audio")}
            >
              {t("Audio")}
            </Chip>
            <Chip
              size="sm"
              color="success"
              variant="flat"
              className="cursor-pointer hover:opacity-80"
              onClick={() => addPresetExtensions("image")}
            >
              {t("Image")}
            </Chip>
            <Chip
              size="sm"
              color="warning"
              variant="flat"
              className="cursor-pointer hover:opacity-80"
              onClick={() => addPresetExtensions("document")}
            >
              {t("Document")}
            </Chip>
            <Chip
              size="sm"
              color="default"
              variant="flat"
              className="cursor-pointer hover:opacity-80"
              onClick={() => addPresetExtensions("archive")}
            >
              {t("Archive")}
            </Chip>
            {(options.extensions?.length ?? 0) > 0 && (
              <Chip
                size="sm"
                color="danger"
                variant="flat"
                className="cursor-pointer hover:opacity-80"
                onClick={clearExtensions}
              >
                {t("Clear All")}
              </Chip>
            )}
          </div>
        </div>

        <Divider />

        {/* Extensions input */}
        <div>
          <ExtensionsInput
            key={options.extensions?.join(",") ?? ""}
            defaultValue={options.extensions}
            label={t("File Extensions")}
            onValueChange={(v) => {
              setOptions({
                ...options,
                extensions: v,
              });
            }}
          />
          <div className="text-xs text-default-400 mt-1">
            <InfoCircleOutlined className="mr-1" />
            {t("Files with these extensions will be considered playable")}
          </div>
        </div>

        {/* File name pattern */}
        <div>
          <Input
            label={t("File Name Pattern")}
            placeholder={t("e.g., .*\\.(mp4|mkv|avi)$")}
            value={options.fileNamePattern || ""}
            onValueChange={(v) => {
              setOptions({
                ...options,
                fileNamePattern: v || undefined,
              });
            }}
          />
          <div className="text-xs text-default-400 mt-1">
            <InfoCircleOutlined className="mr-1" />
            {t("Regex pattern to match playable file names (optional, leave empty for extension-only matching)")}
          </div>
        </div>

        {/* Preview */}
        {((options.extensions?.length ?? 0) > 0 || options.fileNamePattern) && (
          <>
            <Divider />
            <div>
              <div className="text-sm font-medium mb-2">{t("Current Configuration")}</div>
              <div className="p-3 bg-default-100 rounded-lg">
                {(options.extensions?.length ?? 0) > 0 && (
                  <div className="flex flex-wrap gap-1 mb-2">
                    <span className="text-sm text-default-500 mr-2">{t("Extensions")}:</span>
                    {options.extensions?.map((ext) => (
                      <Chip key={ext} size="sm" color="secondary" variant="flat">
                        .{ext}
                      </Chip>
                    ))}
                  </div>
                )}
                {options.fileNamePattern && (
                  <div className="flex items-center gap-2">
                    <span className="text-sm text-default-500">{t("Pattern")}:</span>
                    <Chip size="sm" color="warning" variant="flat">
                      {options.fileNamePattern}
                    </Chip>
                  </div>
                )}
              </div>
            </div>
          </>
        )}
      </div>
    </Modal>
  );
};

PlayableFileSelectorModal.displayName = "PlayableFileSelectorModal";

export default PlayableFileSelectorModal;
