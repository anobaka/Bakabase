"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { ResourceProfile, ProfileSource } from "../sourceProfiles";

import { useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineInfoCircle } from "react-icons/ai";

import { profileSourceApi } from "../sourceProfiles";
import { checkProfileResponse } from "../profileUtils";

import { Modal, Input, Select } from "@/components/bakaui";
import BApi from "@/sdk/BApi";

type Props = {
  profile?: ResourceProfile;
  existingNames?: string[];
  sources?: ProfileSource[];
  defaultSourceDeviceId?: string;
  onSaved?: (profile: ResourceProfile) => void;
  onUpdate?: (profileId: number, updates: Partial<ResourceProfile>) => Promise<void>;
} & DestroyableProps;

function defaultName(existingNames: string[], baseName: string) {
  let number = 1;

  while (existingNames.includes(`${baseName} ${number}`)) number++;

  return `${baseName} ${number}`;
}

export default function ResourceProfileModal({
  profile,
  existingNames = [],
  sources = [],
  defaultSourceDeviceId,
  onSaved,
  onUpdate,
  onDestroyed,
}: Props) {
  const { t } = useTranslation();
  const isEdit = profile !== undefined;
  const [name, setName] = useState(
    () =>
      profile?.name ??
      defaultName(existingNames, t<string>("resourceProfile.label.resourceProfile")),
  );
  const [sourceId, setSourceId] = useState(profile?.sourceDeviceId ?? defaultSourceDeviceId ?? "");
  const [priorityText, setPriorityText] = useState(String(profile?.priority ?? 0));
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const priority = Number(priorityText);
  const priorityValid =
    priorityText.trim() !== "" &&
    Number.isInteger(priority) &&
    priority >= -2147483648 &&
    priority <= 2147483647;
  const editUnavailable = isEdit && (!onUpdate || !Number.isInteger(profile.id) || profile.id <= 0);
  const isValid = name.trim().length > 0 && priorityValid && !editUnavailable;

  const submit = async () => {
    if (!isValid)
      throw new Error(
        t<string>(
          editUnavailable
            ? "resourceProfile.basic.editUnavailable"
            : !name.trim()
              ? "resourceProfile.error.nameRequired"
              : "resourceProfile.basic.invalidPriority",
        ),
      );
    setSaving(true);
    setError("");
    try {
      if (isEdit) {
        // Editing must go through the parent, which merges every other configuration field.
        if (!onUpdate) throw new Error(t<string>("resourceProfile.basic.editUnavailable"));
        await onUpdate(profile.id, { name: name.trim(), priority });
      } else {
        const source = sourceId
          ? await profileSourceApi.info({ sourceDeviceId: sourceId })
          : undefined;

        if (sourceId && (!source || source.deviceId !== sourceId || !source.libraryEpoch))
          throw new Error(t<string>("resourceProfile.source.unavailable"));
        const response = await BApi.resourceProfile.addResourceProfile({
          name: name.trim(),
          priority,
          ...(sourceId
            ? {
                sourceDeviceId: sourceId,
                sourceLibraryEpoch: source?.libraryEpoch,
              }
            : {}),
        });

        checkProfileResponse(response, t<string>("resourceProfile.basic.saveFailed"));
        if (!response.data || !Number.isInteger(response.data.id) || response.data.id <= 0)
          throw new Error(t<string>("resourceProfile.basic.saveFailed"));
        onSaved?.(response.data);
      }
    } catch (failure) {
      setError(
        failure instanceof Error ? failure.message : t<string>("resourceProfile.basic.saveFailed"),
      );
      throw failure;
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal
      defaultVisible
      footer={{
        actions: ["ok", "cancel"],
        okProps: {
          children: t<string>(
            isEdit ? "common.action.save" : "resourceProfile.basic.createAndConfigure",
          ),
          isDisabled: !isValid,
        },
        cancelProps: { children: t<string>("common.action.cancel"), isDisabled: saving },
      }}
      hideCloseButton={saving}
      isDismissable={!saving}
      isKeyboardDismissDisabled={saving}
      size="md"
      title={t<string>(
        isEdit
          ? "resourceProfile.modal.editResourceProfileTitle"
          : "resourceProfile.modal.addResourceProfileTitle",
      )}
      onDestroyed={onDestroyed}
      onOk={submit}
    >
      <p className="text-sm leading-6 text-default-500">
        {t<string>("resourceProfile.basic.description")}
      </p>
      <div className="flex flex-col gap-5 py-2">
        <Select
          disallowEmptySelection
          dataSource={[
            { value: "local", label: t<string>("resourceProfile.source.local") },
            ...sources.map((source) => ({ value: source.deviceId, label: source.name })),
            ...(sourceId && !sources.some((source) => source.deviceId === sourceId)
              ? [{ value: sourceId, label: sourceId }]
              : []),
          ]}
          description={t<string>(
            isEdit ? "resourceProfile.source.fixedHint" : "resourceProfile.source.hint",
          )}
          isDisabled={saving || isEdit}
          label={t<string>("resourceProfile.source.label")}
          selectedKeys={[sourceId || "local"]}
          onSelectionChange={(keys) => {
            const key = [...keys][0];

            if (key) setSourceId(key === "local" ? "" : String(key));
          }}
        />
        <Input
          isRequired
          aria-label={t<string>("resourceProfile.label.name")}
          errorMessage={!name.trim() ? t<string>("resourceProfile.error.nameRequired") : undefined}
          isDisabled={saving}
          isInvalid={!name.trim()}
          label={t<string>("resourceProfile.label.name")}
          labelPlacement="outside"
          placeholder={t<string>("resourceProfile.label.profileName")}
          value={name}
          onValueChange={setName}
        />
        <Input
          aria-label={t<string>("resourceProfile.label.priority")}
          className="max-w-sm"
          description={t<string>("resourceProfile.basic.priorityHint")}
          errorMessage={
            !priorityValid ? t<string>("resourceProfile.basic.invalidPriority") : undefined
          }
          isDisabled={saving}
          isInvalid={!priorityValid}
          label={t<string>("resourceProfile.label.priority")}
          labelPlacement="outside"
          step={1}
          type="number"
          value={priorityText}
          onValueChange={setPriorityText}
        />
      </div>
      {!isEdit && (
        <div className="flex items-start gap-3 rounded-xl bg-primary/5 p-3">
          <AiOutlineInfoCircle aria-hidden className="mt-0.5 shrink-0 text-lg text-primary" />
          <p className="text-sm leading-6 text-default-600">
            {t<string>("resourceProfile.basic.newScopeHint")}
          </p>
        </div>
      )}
      {(error || editUnavailable) && (
        <p className="rounded-lg bg-danger/5 px-3 py-2 text-sm text-danger" role="alert">
          {editUnavailable ? t<string>("resourceProfile.basic.editUnavailable") : error}
        </p>
      )}
    </Modal>
  );
}
