"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { BakabaseAbstractionsModelsDomainResourceProfilePlayableFileOptions } from "@/sdk/Api";

import { useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineFileSearch, AiOutlinePlus } from "react-icons/ai";

import { normalizeProfileExtensions, useProfileModalSave } from "./useProfileModalSave";

import { Button, Input, Modal } from "@/components/bakaui";
import ExtensionsInput from "@/components/ExtensionsInput";

type PlayableFileOptions = BakabaseAbstractionsModelsDomainResourceProfilePlayableFileOptions;
type Props = {
  options?: PlayableFileOptions;
  onSubmit?: (options: PlayableFileOptions) => unknown | Promise<unknown>;
} & DestroyableProps;

const extensionPresets = {
  video: ["mp4", "mkv", "avi", "wmv", "mov", "flv", "webm", "m4v", "rmvb", "rm"],
  audio: ["mp3", "flac", "wav", "aac", "ogg", "wma", "m4a", "ape"],
  image: ["jpg", "jpeg", "png", "gif", "bmp", "webp", "tiff", "svg"],
  document: ["pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "txt"],
  archive: ["zip", "rar", "7z", "tar", "gz"],
};

const PlayableFileSelectorModal = ({ options: propOptions, onSubmit, onDestroyed }: Props) => {
  const { t } = useTranslation();
  const editor = useProfileModalSave(onSubmit, t<string>("resourceProfile.editor.saveFailed"));
  const [options, setOptions] = useState<PlayableFileOptions>(() => ({
    ...propOptions,
    extensions: normalizeProfileExtensions(propOptions?.extensions),
  }));
  // ExtensionsInput owns its text draft. Remount only for preset/clear actions, never while typing.
  const [inputVersion, setInputVersion] = useState(0);
  const extensions = normalizeProfileExtensions(options.extensions);
  const missingExtensions = extensions.length === 0;
  const patternWithoutExtensions = missingExtensions && !!options.fileNamePattern;
  const replaceExtensions = (value: string[]) => {
    setOptions((current) => ({ ...current, extensions: normalizeProfileExtensions(value) }));
    setInputVersion((version) => version + 1);
  };
  const save = () => {
    if (patternWithoutExtensions) return;

    return editor.save({
      ...options,
      extensions,
      fileNamePattern: options.fileNamePattern || undefined,
    });
  };

  return (
    <Modal
      classNames={{ base: "max-w-2xl", body: "gap-5", footer: "border-t border-default-200/60" }}
      footer={
        <div className="flex w-full items-center justify-end gap-2">
          <Button isDisabled={editor.saving} variant="light" onPress={editor.close}>
            {t<string>("common.action.cancel")}
          </Button>
          <Button
            color="primary"
            isDisabled={patternWithoutExtensions}
            isLoading={editor.saving}
            onPress={save}
          >
            {t<string>("common.action.save")}
          </Button>
        </div>
      }
      hideCloseButton={editor.saving}
      isDismissable={!editor.saving}
      isKeyboardDismissDisabled={editor.saving}
      size="3xl"
      title={t<string>("resourceProfile.modal.playableFileOptionsTitle")}
      visible={editor.visible}
      onClose={editor.close}
      onDestroyed={onDestroyed}
    >
      <p className="text-sm leading-6 text-default-600">
        {t<string>("resourceProfile.playable.description")}
      </p>
      <fieldset className="m-0 flex min-w-0 flex-col gap-5 border-0 p-0" disabled={editor.saving}>
        <section className="space-y-2.5">
          <h3 className="text-sm font-medium">
            {t<string>("resourceProfile.label.quickAddPresets")}
          </h3>
          <div className="flex flex-wrap gap-2">
            {(Object.keys(extensionPresets) as (keyof typeof extensionPresets)[]).map((preset) => (
              <Button
                key={preset}
                isDisabled={editor.saving}
                size="sm"
                startContent={<AiOutlinePlus aria-hidden />}
                variant="flat"
                onPress={() => replaceExtensions([...extensions, ...extensionPresets[preset]])}
              >
                {t<string>(`resourceProfile.label.${preset}`)}
              </Button>
            ))}
          </div>
          <p className="text-xs leading-5 text-default-500">
            {t<string>("resourceProfile.playable.presetsHint")}
          </p>
        </section>
        <section className="space-y-2">
          <div className="flex items-center justify-between gap-2">
            <h3 className="text-sm font-medium">
              {t<string>("resourceProfile.playable.extensionsTitle")}
            </h3>
            {!missingExtensions && (
              <Button
                color="danger"
                isDisabled={editor.saving}
                size="sm"
                variant="light"
                onPress={() => replaceExtensions([])}
              >
                {t<string>("resourceProfile.action.clearAll")}
              </Button>
            )}
          </div>
          <ExtensionsInput
            key={inputVersion}
            defaultValue={options.extensions}
            label={t<string>("resourceProfile.label.fileExtensions")}
            minRows={2}
            onValueChange={(extensions) => setOptions((current) => ({ ...current, extensions }))}
          />
          <p className="text-xs leading-5 text-default-500">
            {t<string>("resourceProfile.playable.extensionsHint")}
          </p>
        </section>
        <details
          className="group border-t border-default-200/60 pt-3"
          open={propOptions?.fileNamePattern ? true : undefined}
        >
          <summary className="flex cursor-pointer list-none items-center gap-2 py-1 text-sm font-medium text-default-600">
            <AiOutlineFileSearch aria-hidden className="text-lg" />
            {t<string>("resourceProfile.playable.patternTitle")}
            <span aria-hidden className="ml-auto text-default-400 group-open:rotate-90">
              ›
            </span>
          </summary>
          <div className="mt-3 space-y-2">
            <Input
              description={t<string>("resourceProfile.playable.patternHint")}
              errorMessage={t<string>("resourceProfile.playable.patternNeedsExtensions")}
              isDisabled={editor.saving}
              isInvalid={patternWithoutExtensions}
              label={t<string>("resourceProfile.label.fileNamePattern")}
              placeholder={t<string>("resourceProfile.input.fileNamePatternPlaceholder")}
              value={options.fileNamePattern || ""}
              onValueChange={(fileNamePattern) =>
                setOptions((current) => ({
                  ...current,
                  fileNamePattern: fileNamePattern || undefined,
                }))
              }
            />
          </div>
        </details>
      </fieldset>
      {missingExtensions && (
        <div className="flex gap-3 rounded-xl bg-default-50 px-4 py-3">
          <AiOutlineFileSearch aria-hidden className="mt-0.5 shrink-0 text-xl text-default-400" />
          <div className="space-y-1">
            <p className="text-sm font-medium">
              {t<string>("resourceProfile.playable.emptyTitle")}
            </p>
            <p className="text-xs leading-5 text-default-500">
              {t<string>("resourceProfile.playable.emptyDescription")}
            </p>
          </div>
        </div>
      )}
      {editor.error && (
        <p className="rounded-lg bg-danger/10 px-3 py-2 text-sm text-danger" role="alert">
          {editor.error}
        </p>
      )}
    </Modal>
  );
};

PlayableFileSelectorModal.displayName = "PlayableFileSelectorModal";
export default PlayableFileSelectorModal;
