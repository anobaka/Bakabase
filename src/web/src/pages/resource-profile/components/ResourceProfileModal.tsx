"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { BakabaseServiceModelsViewResourceProfileViewModel } from "@/sdk/Api";

import { useState } from "react";
import { useTranslation } from "react-i18next";

import { Modal, Input } from "@/components/bakaui";
import BApi from "@/sdk/BApi";

type ResourceProfile = BakabaseServiceModelsViewResourceProfileViewModel;

type Props = {
  profile?: ResourceProfile;
  existingNames?: string[];
  onSaved?: () => void;
  /** For editing existing profiles - uses parent's update logic to preserve all fields */
  onUpdate?: (profileId: number, updates: Partial<ResourceProfile>) => Promise<void>;
} & DestroyableProps;

// Generate default name like "资源档案 1", "资源档案 2", etc.
const generateDefaultName = (existingNames: string[], t: (key: string) => string): string => {
  const baseNameKey = "resourceProfile.label.resourceProfile";
  const baseName = t(baseNameKey);
  let n = 1;
  while (existingNames.includes(`${baseName} ${n}`)) {
    n++;
  }
  return `${baseName} ${n}`;
};

const ResourceProfileModal = ({ profile, existingNames = [], onSaved, onUpdate, onDestroyed }: Props) => {
  const { t } = useTranslation();
  const isEdit = !!profile?.id;

  const [name, setName] = useState(
    profile?.name || (isEdit ? "" : generateDefaultName(existingNames, t))
  );
  const [priority, setPriority] = useState(profile?.priority ?? 0);

  const [saving, setSaving] = useState(false);

  const handleSubmit = async () => {
    if (!name.trim()) {
      return;
    }

    setSaving(true);
    try {
      if (isEdit && profile?.id && onUpdate) {
        // Use onUpdate callback which preserves all fields via profilesRef
        await onUpdate(profile.id, { name, priority });
      } else {
        // For creating new profile, only need name and priority
        const inputModel = {
          name,
          priority,
        };
        await BApi.resourceProfile.addResourceProfile(inputModel as any);
      }
      onSaved?.();
      onDestroyed?.();
    } catch (e) {
      console.error("Failed to save resource profile", e);
    } finally {
      setSaving(false);
    }
  };

  const isValid = name.trim() !== "";

  return (
    <Modal
      defaultVisible
      okProps={{
        isDisabled: !isValid,
        isLoading: saving,
      }}
      size="md"
      title={isEdit ? t("resourceProfile.modal.editResourceProfileTitle") : t("resourceProfile.modal.addResourceProfileTitle")}
      onDestroyed={onDestroyed}
      onOk={handleSubmit}
    >
      <div className="flex flex-col gap-4">
        <Input
          isRequired
          errorMessage={!name.trim() ? t("resourceProfile.error.nameRequired") : ""}
          isInvalid={!name.trim()}
          label={t("resourceProfile.label.name")}
          placeholder={t("resourceProfile.label.profileName")}
          value={name}
          onValueChange={setName}
        />
        <Input
          description={t("resourceProfile.tip.higherPriorityPrecedence")}
          label={t("resourceProfile.label.priority")}
          placeholder="0"
          type="number"
          value={String(priority)}
          onValueChange={(v) => setPriority(parseInt(v) || 0)}
        />
        {!isEdit && (
          <p className="text-xs text-default-400">
            {t("resourceProfile.tip.otherSettingsAfterCreation")}
          </p>
        )}
      </div>
    </Modal>
  );
};

ResourceProfileModal.displayName = "ResourceProfileModal";

export default ResourceProfileModal;
