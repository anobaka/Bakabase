"use client";

import type { SourceKind } from "./sourcePicker";

import { useState } from "react";
import { useTranslation } from "react-i18next";
import {
  AiOutlineDownload,
  AiOutlineFileText,
  AiOutlineGlobal,
  AiOutlinePlus,
} from "react-icons/ai";
import { FaMagnet } from "react-icons/fa";

import { MAX_SOURCE_LENGTH, sourceMethods, validateAcquisitionSource } from "./sourcePicker";

import BApi from "@/sdk/BApi";
import { AcquisitionLeadKind, AcquisitionLeadOrigin } from "@/sdk/constants";
import { Button, Chip, Input, Modal, Textarea } from "@/components/bakaui";

interface Props {
  resourceId: number;
  onAdded: () => void;
  onDestroyed?: () => void;
}

const icons = {
  directUrl: AiOutlineDownload,
  sharedPage: AiOutlineGlobal,
  sharedDocument: AiOutlineFileText,
  magnet: FaMagnet,
};
const k = (key: string) => `acquisition.sourcePicker.${key}`;

const AddSourceModal = ({ resourceId, onAdded, onDestroyed }: Props) => {
  const { t } = useTranslation();
  const [visible, setVisible] = useState(true);
  const [kind, setKind] = useState<SourceKind>();
  const [value, setValue] = useState("");
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string>();
  const method = sourceMethods.find((item) => item.kind === kind);
  const validation = validateAcquisitionSource(kind, value);

  const save = async () => {
    if (validation || kind === undefined || saving) return;
    setSaving(true);
    setSaveError(undefined);
    try {
      const response = await BApi.resource.addResourceAcquisitionLead(resourceId, {
        kind,
        value: value.trim(),
        origin: AcquisitionLeadOrigin.User,
      });

      if (response.code) {
        setSaveError(response.message || t<string>(k("saveFailed")));

        return;
      }
      onAdded();
      setVisible(false);
    } catch (error) {
      setSaveError(error instanceof Error ? error.message : t<string>(k("saveFailed")));
    } finally {
      setSaving(false);
    }
  };

  const inputProps = method
    ? {
        isRequired: true,
        isDisabled: saving,
        isInvalid: !!value.trim() && !!validation,
        errorMessage: validation ? t<string>(k(`validation.${validation}`)) : undefined,
        label: t<string>(k(`${method.id}.label`)),
        placeholder: t<string>(k(`${method.id}.placeholder`)),
        maxLength: MAX_SOURCE_LENGTH,
        value,
        onValueChange: (next: string) => {
          setValue(next);
          setSaveError(undefined);
        },
      }
    : undefined;

  return (
    <Modal
      footer={
        <div className="flex w-full justify-end gap-2">
          <Button isDisabled={saving} variant="light" onPress={() => setVisible(false)}>
            {t<string>(k("cancel"))}
          </Button>
          <Button
            color="primary"
            isDisabled={!!validation || saving}
            isLoading={saving}
            startContent={<AiOutlinePlus aria-hidden />}
            onPress={save}
          >
            {t<string>(k("save"))}
          </Button>
        </div>
      }
      hideCloseButton={saving}
      isDismissable={!saving}
      size="xl"
      title={t<string>(k("title"))}
      visible={visible}
      onClose={() => setVisible(false)}
      onDestroyed={onDestroyed}
    >
      <div className="flex flex-col gap-4">
        <p className="text-sm text-default-600">{t<string>(k("intro"))}</p>
        <div
          aria-label={t<string>(k("methods"))}
          className="grid gap-2 sm:grid-cols-2"
          role="group"
        >
          {sourceMethods.map((item) => {
            const Icon = icons[item.id];
            const selected = kind === item.kind;

            return (
              <Button
                key={item.id}
                aria-label={t<string>(k(`${item.id}.title`))}
                aria-pressed={selected}
                className="h-auto min-w-0 justify-start whitespace-normal p-3 text-left"
                color={selected ? "primary" : "default"}
                isDisabled={saving}
                variant="flat"
                onPress={() => {
                  if (selected) return;
                  setKind(item.kind);
                  setValue("");
                  setSaveError(undefined);
                }}
              >
                <span className="flex w-full flex-col gap-1.5">
                  <span className="flex items-center gap-2">
                    <Icon aria-hidden className="shrink-0 text-lg" />
                    <span className="min-w-0 flex-1 font-medium">
                      {t<string>(k(`${item.id}.title`))}
                    </span>
                    <Chip size="sm" variant="flat">
                      {t<string>(k("builtin"))}
                    </Chip>
                  </span>
                  <span className="text-xs text-default-500">
                    {t<string>(k(`${item.id}.summary`))}
                  </span>
                </span>
              </Button>
            );
          })}
        </div>
        {method && (
          <div className="flex flex-col gap-3">
            <p className="rounded-lg bg-primary/5 p-3 text-sm text-default-600">
              {t<string>(k(`${method.id}.description`))}
            </p>
            {kind === AcquisitionLeadKind.SharedDocument ? (
              <Textarea {...inputProps} minRows={5} />
            ) : (
              <Input {...inputProps} />
            )}
          </div>
        )}
        <p className="text-xs text-default-500">{t<string>(k("scope"))}</p>
        {saveError && (
          <p className="text-sm text-danger" role="alert">
            {saveError}
          </p>
        )}
      </div>
    </Modal>
  );
};

export default AddSourceModal;
